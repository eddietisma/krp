﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Krp.Dns;

public interface IDnsHandler
{
    Task UpdateAsync(List<string> hostnames);
}

public class DnsHostsHandler : IDnsHandler
{
    private const string MARKER_START = "# Added by krp";
    private const string MARKER_END = "# End of section";
    private readonly DnsHostsOptions _options;
    private readonly ILogger<DnsHostsHandler> _logger;

    public DnsHostsHandler(IOptions<DnsHostsOptions> options, ILogger<DnsHostsHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task UpdateAsync(List<string> hostnames)
    {
        try
        {
            if (!File.Exists(_options.Path))
            {
                _logger.LogError("Error when updating DNS. File not found: {path}", _options.Path);
                return;
            }

            var originalLines = await File.ReadAllLinesAsync(_options.Path);
            var lines = originalLines.ToList();

            _logger.LogInformation("Loaded hosts file '{path}' ({count} entries)", _options.Path, lines.Count);

            var startIndex = lines.FindIndex(line => line.Trim() == MARKER_START);
            if (startIndex != -1)
            {
                var endIndex = lines.FindIndex(startIndex, line => line.Trim() == MARKER_END);
                if (endIndex != -1 && endIndex > startIndex)
                {
                    if (lines[startIndex - 1] == "")
                    {
                        startIndex--;
                    }

                    // Remove old section.
                    lines.RemoveRange(startIndex, endIndex - startIndex + 1);
                }
            }

            lines.Add("");
            lines.Add(MARKER_START);
            lines.AddRange(hostnames);
            lines.Add(MARKER_END);

            var hasChanges = !lines.SequenceEqual(originalLines);
            if (!hasChanges)
            {
                _logger.LogInformation("Skipped updating DNS due to no changes in HOSTS file");
                return;
            }

            var backupPath = _options.Path + $".bak.krp_{DateTime.Now:yyyyMMdd_HHmmss}";

            File.Copy(_options.Path, backupPath, overwrite: true);
            _logger.LogInformation("Backup created '{path}'", backupPath);

            await File.WriteAllLinesAsync(_options.Path, lines, Encoding.UTF8);
            _logger.LogInformation("Successfully updated hosts file ({count} entries)", hostnames.Count);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error when updating DNS");
            throw;
        }
    }
}