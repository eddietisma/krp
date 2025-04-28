using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Routing;

public interface IDnsHandler
{
    Task UpdateAsync(List<string> hostnames);
}

public class DnsWindowsHostsHandler : IDnsHandler
{
    private const string MARKER_START = "# Added by krp";
    private const string MARKER_END = "# End of section";
    private readonly DnsWindowsHostsOptions _options;
    private readonly ILogger<DnsWindowsHostsHandler> _logger;

    public DnsWindowsHostsHandler(IOptions<DnsWindowsHostsOptions> options, ILogger<DnsWindowsHostsHandler> logger)
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

            var lines = (await File.ReadAllLinesAsync(_options.Path)).ToList();
            _logger.LogInformation("Read {count} lines from HOSTS file", lines.Count);

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

            var hasChanges = !(await File.ReadAllLinesAsync(_options.Path)).ToList().SequenceEqual(lines);
            if (!hasChanges)
            {
                _logger.LogInformation("Skipped updating DNS due to no changes in HOSTS file");
                return;
            }

            var backupPath = _options.Path + $".bak.krp_{DateTime.Now:yyyyMMdd_HHmmss}";

            File.Copy(_options.Path, backupPath, overwrite: true);
            _logger.LogInformation("Backup created at: {path}", backupPath);

            await File.WriteAllLinesAsync(_options.Path, lines, Encoding.UTF8);
            _logger.LogInformation("HOSTS file updated with {count} entries", hostnames.Count);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error when updating DNS");
            throw;
        }
    }
}