using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using Serilog.Events;

namespace Content.Server._Duty.Logging;

/// <summary>
/// _Duty: файловый лог-хендлер только для Error/Fatal, подключается только в Release-сборке
/// (см. подключение через "#if RELEASE" в <see cref="Content.Server.Entry.EntryPoint"/>).
/// Не пишет в реальном времени — строки копятся в памяти и сбрасываются на диск фоновым
/// таймером раз в <see cref="FlushIntervalSeconds"/> секунд (не в игровом треде, так что сам
/// <see cref="Log"/> не блокирует тик), плюс немедленный сброс на Fatal и при накоплении
/// <see cref="MaxBufferedLines"/> строк — чтобы не грузить сервер частым I/O, но и не терять
/// много при краше. Один файл на календарный день (UTC), старые файлы старше
/// <see cref="RetentionDays"/> дней подчищаются при старте — см. <see cref="DutyErrorLogPaths"/>.
///
/// Важно: обработка собственных ошибок записи идёт напрямую в Console.WriteLine, а не через
/// sawmill — иначе Error/Fatal от неудачной записи попадёт обратно в этот же хендлер и зациклится.
/// </summary>
internal sealed class DutyErrorLogHandler : ILogHandler, IDisposable
{
    private const double FlushIntervalSeconds = 5;
    private const int MaxBufferedLines = 100;
    private const int RetentionDays = 14;

    private readonly object _lock = new();
    private readonly List<string> _buffer = new();
    private readonly Timer _timer;
    private readonly int _pid = Environment.ProcessId;

    private StreamWriter? _writer;
    private DateTime _writerDate;
    private bool _disposed;

    public DutyErrorLogHandler()
    {
        DutyErrorLogPaths.CleanupOldLogs(RetentionDays);

        _timer = new Timer(FlushIntervalSeconds * 1000);
        _timer.Elapsed += (_, _) => Flush();
        _timer.Start();

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public void Log(string sawmillName, LogEvent message)
    {
        var level = message.Level.ToRobust();
        if (level < LogLevel.Error)
            return;

        var line = FormatLine(sawmillName, level, message);
        bool shouldFlush;

        lock (_lock)
        {
            if (_disposed)
                return;

            _buffer.Add(line);
            shouldFlush = level >= LogLevel.Fatal || _buffer.Count >= MaxBufferedLines;
        }

        if (shouldFlush)
            Flush();
    }

    private static string FormatLine(string sawmillName, LogLevel level, LogEvent message)
    {
        var name = LogMessage.LogLevelToName(level);
        var sb = new StringBuilder(256);
        sb.Append(DateTime.UtcNow.ToString("o"));
        sb.Append(" [").Append(name).Append("] ");
        sb.Append(sawmillName).Append(": ").AppendLine(message.RenderMessage());

        if (message.Exception != null)
            sb.AppendLine(message.Exception.ToString());

        return sb.ToString();
    }

    private void Flush()
    {
        lock (_lock)
        {
            if (_buffer.Count == 0)
                return;

            try
            {
                var writer = GetWriter();
                foreach (var line in _buffer)
                    writer.Write(line);
                writer.Flush();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[DutyErrorLogHandler] Failed to write duty error log: {e}");
            }
            finally
            {
                _buffer.Clear();
            }
        }
    }

    // Вызывается только изнутри Flush(), которая уже держит _lock.
    private StreamWriter GetWriter()
    {
        var today = DateTime.UtcNow.Date;
        if (_writer != null && _writerDate == today)
            return _writer;

        _writer?.Dispose();

        var path = DutyErrorLogPaths.GetLogFilePath(today);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var isNewFile = !File.Exists(path);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
        _writer = new StreamWriter(stream, EncodingHelpers.UTF8);
        _writerDate = today;

        var marker = isNewFile ? "new file" : "appending";
        _writer.WriteLine($"==== SESSION START {DateTime.UtcNow:o} pid={_pid} ({marker}) ====");

        return _writer;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Flush();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _timer.Stop();
        _timer.Dispose();

        // Финальный сброс накопленного буфера перед закрытием файла.
        Flush();
        _writer?.Dispose();
    }
}
