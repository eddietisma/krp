// -----------------------------------------------------------
// Portions of this file are derived from the .NET runtime:
//   https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.Logging.Console
//   • AnsiParser.cs 
//   • SimpleConsoleFormatter.cs
//   • ConsoleLoggerHelpers.cs 
// -----------------------------------------------------------
// https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Console/ConsoleUtils.cs
// https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Console/src/AnsiParser.cs
// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Encodings.Web/src/System/IO/TextWriterExtensions.cs

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace Krp.Logging;

internal static class ConsoleUtils
{
    /// <summary>Whether to output ansi color strings.</summary>
    private static volatile int s_emitAnsiColorCodes = -1;

    /// <summary>Get whether to emit ANSI color codes.</summary>
    public static bool EmitAnsiColorCodes
    {
        get
        {
            // The flag starts at -1.  If it's no longer -1, it's 0 or 1 to represent false or true.
            var emitAnsiColorCodes = s_emitAnsiColorCodes;
            if (emitAnsiColorCodes != -1)
            {
                return Convert.ToBoolean(emitAnsiColorCodes);
            }

            // We've not yet computed whether to emit codes or not.  Do so now.  We may race with
            // other threads, and that's ok; this is idempotent unless someone is currently changing
            // the value of the relevant environment variables, in which case behavior here is undefined.

            // By default, we emit ANSI color codes if output isn't redirected, and suppress them if output is redirected.
            var enabled = !Console.IsOutputRedirected;

            if (enabled)
            {
                // We subscribe to the informal standard from https://no-color.org/.  If we'd otherwise emit
                // ANSI color codes but the NO_COLOR environment variable is set, disable emitting them.
                enabled = Environment.GetEnvironmentVariable("NO_COLOR") is null;
            }
            else
            {
                // We also support overriding in the other direction.  If we'd otherwise avoid emitting color
                // codes but the DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION environment variable is
                // set to 1 or true, enable color.
                var envVar = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION");
                enabled = envVar is not null && (envVar == "1" || envVar.Equals("true", StringComparison.OrdinalIgnoreCase));
            }

            // Store and return the computed answer.
            s_emitAnsiColorCodes = Convert.ToInt32(enabled);
            return enabled;
        }
    }
}

internal sealed class AnsiParser
{
    private readonly Action<string, int, int, ConsoleColor?, ConsoleColor?> _onParseWrite;
    public AnsiParser(Action<string, int, int, ConsoleColor?, ConsoleColor?> onParseWrite)
    {
        ArgumentNullException.ThrowIfNull(onParseWrite);

        _onParseWrite = onParseWrite;
    }

    /// <summary>
    /// Parses a subset of display attributes
    /// Set Display Attributes
    /// Set Attribute Mode [{attr1};...;{attrn}m
    /// Sets multiple display attribute settings. The following lists standard attributes that are getting parsed:
    /// 1 Bright
    /// Foreground Colours
    /// 30 Black
    /// 31 Red
    /// 32 Green
    /// 33 Yellow
    /// 34 Blue
    /// 35 Magenta
    /// 36 Cyan
    /// 37 White
    /// Background Colours
    /// 40 Black
    /// 41 Red
    /// 42 Green
    /// 43 Yellow
    /// 44 Blue
    /// 45 Magenta
    /// 46 Cyan
    /// 47 White
    /// </summary>
    public void Parse(string message)
    {
        var startIndex = -1;
        var length = 0;
        int escapeCode;
        ConsoleColor? foreground = null;
        ConsoleColor? background = null;
        var span = message.AsSpan();
        const char EscapeChar = '\e';
        ConsoleColor? color = null;
        var isBright = false;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == EscapeChar && span.Length >= i + 4 && span[i + 1] == '[')
            {
                if (span[i + 3] == 'm')
                {
                    // Example: \e[1m
                    if (IsDigit(span[i + 2]))
                    {
                        escapeCode = span[i + 2] - '0';
                        if (startIndex != -1)
                        {
                            _onParseWrite(message, startIndex, length, background, foreground);
                            startIndex = -1;
                            length = 0;
                        }
                        if (escapeCode == 1)
                        {
                            isBright = true;
                        }

                        i += 3;
                        continue;
                    }
                }
                else if (span.Length >= i + 5 && span[i + 4] == 'm')
                {
                    // Example: \e[40m
                    if (IsDigit(span[i + 2]) && IsDigit(span[i + 3]))
                    {
                        escapeCode = ((span[i + 2] - '0') * 10) + (span[i + 3] - '0');
                        if (startIndex != -1)
                        {
                            _onParseWrite(message, startIndex, length, background, foreground);
                            startIndex = -1;
                            length = 0;
                        }
                        if (TryGetForegroundColor(escapeCode, isBright, out color))
                        {
                            foreground = color;
                            isBright = false;
                        }
                        else if (TryGetBackgroundColor(escapeCode, out color))
                        {
                            background = color;
                        }
                        i += 4;
                        continue;
                    }
                }
            }
            if (startIndex == -1)
            {
                startIndex = i;
            }
            var nextEscapeIndex = -1;
            if (i < message.Length - 1)
            {
                nextEscapeIndex = message.IndexOf(EscapeChar, i + 1);
            }
            if (nextEscapeIndex < 0)
            {
                length = message.Length - startIndex;
                break;
            }
            length = nextEscapeIndex - startIndex;
            i = nextEscapeIndex - 1;
        }
        if (startIndex != -1)
        {
            _onParseWrite(message, startIndex, length, background, foreground);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char c) => (uint)(c - '0') <= '9' - '0';

    internal const string DefaultForegroundColor = "\e[39m\e[22m"; // reset to default foreground color
    internal const string DefaultBackgroundColor = "\e[49m"; // reset to the background color

    internal static string GetForegroundColorEscapeCode(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "\e[30m",
            ConsoleColor.DarkRed => "\e[31m",
            ConsoleColor.DarkGreen => "\e[32m",
            ConsoleColor.DarkYellow => "\e[33m",
            ConsoleColor.DarkBlue => "\e[34m",
            ConsoleColor.DarkMagenta => "\e[35m",
            ConsoleColor.DarkCyan => "\e[36m",
            ConsoleColor.Gray => "\e[37m",
            ConsoleColor.Red => "\e[1m\e[31m",
            ConsoleColor.Green => "\e[1m\e[32m",
            ConsoleColor.Yellow => "\e[1m\e[33m",
            ConsoleColor.Blue => "\e[1m\e[34m",
            ConsoleColor.Magenta => "\e[1m\e[35m",
            ConsoleColor.Cyan => "\e[1m\e[36m",
            ConsoleColor.White => "\e[1m\e[37m",
            _ => DefaultForegroundColor // default foreground color
        };
    }

    internal static string GetBackgroundColorEscapeCode(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "\e[40m",
            ConsoleColor.DarkRed => "\e[41m",
            ConsoleColor.DarkGreen => "\e[42m",
            ConsoleColor.DarkYellow => "\e[43m",
            ConsoleColor.DarkBlue => "\e[44m",
            ConsoleColor.DarkMagenta => "\e[45m",
            ConsoleColor.DarkCyan => "\e[46m",
            ConsoleColor.Gray => "\e[47m",
            _ => DefaultBackgroundColor // Use default background color
        };
    }

    private static bool TryGetForegroundColor(int number, bool isBright, out ConsoleColor? color)
    {
        color = number switch
        {
            30 => ConsoleColor.Black,
            31 => isBright ? ConsoleColor.Red : ConsoleColor.DarkRed,
            32 => isBright ? ConsoleColor.Green : ConsoleColor.DarkGreen,
            33 => isBright ? ConsoleColor.Yellow : ConsoleColor.DarkYellow,
            34 => isBright ? ConsoleColor.Blue : ConsoleColor.DarkBlue,
            35 => isBright ? ConsoleColor.Magenta : ConsoleColor.DarkMagenta,
            36 => isBright ? ConsoleColor.Cyan : ConsoleColor.DarkCyan,
            37 => isBright ? ConsoleColor.White : ConsoleColor.Gray,
            _ => null
        };
        return color != null || number == 39;
    }

    private static bool TryGetBackgroundColor(int number, out ConsoleColor? color)
    {
        color = number switch
        {
            40 => ConsoleColor.Black,
            41 => ConsoleColor.DarkRed,
            42 => ConsoleColor.DarkGreen,
            43 => ConsoleColor.DarkYellow,
            44 => ConsoleColor.DarkBlue,
            45 => ConsoleColor.DarkMagenta,
            46 => ConsoleColor.DarkCyan,
            47 => ConsoleColor.Gray,
            _ => null
        };
        return color != null || number == 49;
    }
}

internal static class TextWriterExtensions
{
    public static void WriteColoredMessage(this TextWriter textWriter, string message, ConsoleColor? background, ConsoleColor? foreground)
    {
        // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
        if (background.HasValue)
        {
            textWriter.Write(AnsiParser.GetBackgroundColorEscapeCode(background.Value));
        }
        if (foreground.HasValue)
        {
            textWriter.Write(AnsiParser.GetForegroundColorEscapeCode(foreground.Value));
        }
        textWriter.Write(message);
        if (foreground.HasValue)
        {
            textWriter.Write(AnsiParser.DefaultForegroundColor); // reset to default foreground color
        }
        if (background.HasValue)
        {
            textWriter.Write(AnsiParser.DefaultBackgroundColor); // reset to the background color
        }
    }
}

public sealed class KrpConsoleFormatter : ConsoleFormatter, IDisposable
{
    private static string ShortenCategory(string category)
    {
        // Handles namespaces (.), nested classes (+) and generic arity (`
        //    e.g.  "My.App.Services.UserService+Nested`1"  -> "Nested`1"
        //          "My.App.Services.UserService"          -> "UserService"
        //          "UserService"                          -> "UserService"
        if (string.IsNullOrEmpty(category))
        {
            return category;
        }

        var lastDot = category.LastIndexOf('.');
        var lastPlus = category.LastIndexOf('+');
        var idx = Math.Max(lastDot, lastPlus);

        return idx >= 0 ? category[(idx + 1)..] : category;
    }


    private const string LoglevelPadding = ": ";
    private static readonly string _messagePadding = new string(' ', GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);
    private static readonly string _newLineWithMessagePadding = Environment.NewLine + _messagePadding;
#if NET
    private static bool IsAndroidOrAppleMobile => OperatingSystem.IsAndroid() ||
                                                  OperatingSystem.IsTvOS() ||
                                                  OperatingSystem.IsIOS(); // returns true on MacCatalyst
#else
    private static bool IsAndroidOrAppleMobile => false;
#endif
    private readonly IDisposable _optionsReloadToken;

    public KrpConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
        : base("krp")
    {
        ReloadLoggerOptions(options.CurrentValue);
        _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
    }

    [MemberNotNull(nameof(FormatterOptions))]
    private void ReloadLoggerOptions(SimpleConsoleFormatterOptions options)
    {
        FormatterOptions = options;
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }

    internal SimpleConsoleFormatterOptions FormatterOptions { get; set; }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
    {
        if (logEntry.State is BufferedLogRecord bufferedRecord)
        {
            var message = bufferedRecord.FormattedMessage ?? string.Empty;
            WriteInternal(null, textWriter, message, bufferedRecord.LogLevel, bufferedRecord.EventId.Id, bufferedRecord.Exception, logEntry.Category, bufferedRecord.Timestamp);
        }
        else
        {
            var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }

            // We extract most of the work into a non-generic method to save code size. If this was left in the generic
            // method, we'd get generic specialization for all TState parameters, but that's unnecessary.
            WriteInternal(scopeProvider, textWriter, message, logEntry.LogLevel, logEntry.EventId.Id, logEntry.Exception?.ToString(), logEntry.Category, GetCurrentDateTime());
        }
    }

    private void WriteInternal(IExternalScopeProvider scopeProvider, TextWriter textWriter, string message, LogLevel logLevel, int eventId, string exception, string category, DateTimeOffset stamp)
    {
        var logLevelColors = GetLogLevelConsoleColors(logLevel);
        var logLevelString = GetLogLevelString(logLevel);
        string timestamp = null;
        var timestampFormat = FormatterOptions.TimestampFormat;
        if (timestampFormat != null)
        {
            timestamp = stamp.ToString(timestampFormat);
        }
        if (timestamp is not null)
        {
            textWriter.Write(timestamp);
        }
        if (logLevelString is not null)
        {
            textWriter.WriteColoredMessage(logLevelString, logLevelColors.Background, logLevelColors.Foreground);
        }

        var singleLine = FormatterOptions.SingleLine;

        // Example:
        // info: ConsoleApp.Program[10]
        //       Request received

        // category and event id
        textWriter.Write(LoglevelPadding);
        textWriter.Write(category);
        //textWriter.Write(ShortenCategory(category));
        textWriter.Write('[');

#if NET
        Span<char> span = stackalloc char[10];
        if (eventId.TryFormat(span, out var charsWritten))
        {
            textWriter.Write(span.Slice(0, charsWritten));
        }
        else
#endif
        {
            textWriter.Write(eventId.ToString());
        }

        textWriter.Write(']');
        if (!singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }

        // scope information
        WriteScopeInformation(textWriter, scopeProvider, singleLine);
        WriteMessage(textWriter, message, singleLine);

        // Example:
        // System.InvalidOperationException
        //    at Namespace.Class.Function() in File:line X
        if (exception != null)
        {
            // exception message
            WriteMessage(textWriter, exception, singleLine);
        }

        if (singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }
    }

    private static void WriteMessage(TextWriter textWriter, string message, bool singleLine)
    {
        if (!string.IsNullOrEmpty(message))
        {
            if (singleLine)
            {
                textWriter.Write(' ');
                WriteReplacing(textWriter, Environment.NewLine, " ", message);
            }
            else
            {
                textWriter.Write(_messagePadding);
                WriteReplacing(textWriter, Environment.NewLine, _newLineWithMessagePadding, message);
                textWriter.Write(Environment.NewLine);
            }
        }

        static void WriteReplacing(TextWriter writer, string oldValue, string newValue, string message)
        {
            var newMessage = message.Replace(oldValue, newValue);
            writer.Write(newMessage);
        }
    }

    private DateTimeOffset GetCurrentDateTime()
    {
        return FormatterOptions.TimestampFormat != null
            ? FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now
            : DateTimeOffset.MinValue;
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private ConsoleColors GetLogLevelConsoleColors(LogLevel logLevel)
    {
        // We shouldn't be outputting color codes for Android/Apple mobile platforms,
        // they have no shell (adb shell is not meant for running apps) and all the output gets redirected to some log file.
        var disableColors = FormatterOptions.ColorBehavior == LoggerColorBehavior.Disabled ||
            (FormatterOptions.ColorBehavior == LoggerColorBehavior.Default && (!ConsoleUtils.EmitAnsiColorCodes || IsAndroidOrAppleMobile));
        if (disableColors)
        {
            return new ConsoleColors(null, null);
        }
        // We must explicitly set the background color if we are setting the foreground color,
        // since just setting one can look bad on the users console.
        return logLevel switch
        {
            LogLevel.Trace => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Debug => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Information => new ConsoleColors(ConsoleColor.DarkGreen, ConsoleColor.Black),
            LogLevel.Warning => new ConsoleColors(ConsoleColor.Yellow, ConsoleColor.Black),
            LogLevel.Error => new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkRed),
            LogLevel.Critical => new ConsoleColors(ConsoleColor.White, ConsoleColor.DarkRed),
            _ => new ConsoleColors(null, null)
        };
    }

    private void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider scopeProvider, bool singleLine)
    {
        if (FormatterOptions.IncludeScopes && scopeProvider != null)
        {
            var paddingNeeded = !singleLine;
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (paddingNeeded)
                {
                    paddingNeeded = false;
                    state.Write(_messagePadding);
                    state.Write("=> ");
                }
                else
                {
                    state.Write(" => ");
                }
                state.Write(scope);
            }, textWriter);

            if (!paddingNeeded && !singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }
    }

    private readonly struct ConsoleColors
    {
        public ConsoleColors(ConsoleColor? foreground, ConsoleColor? background)
        {
            Foreground = foreground;
            Background = background;
        }

        public ConsoleColor? Foreground { get; }

        public ConsoleColor? Background { get; }
    }
}
