using System;
using System.Globalization;
using System.IO;

namespace Content.Server._Duty.Logging;

/// <summary>
/// _Duty: пути и обслуживание файлов лога ошибок (см. <see cref="DutyErrorLogHandler"/>).
/// Каталог логов лежит рядом с exe (AppContext.BaseDirectory) — рядом с движковым
/// bin/Content.Server/&lt;config&gt;/logs/, но в отдельной папке, чтобы не смешивать с обычным
/// логом движка. Один файл на календарный день (UTC), имя вида "error-yyyy-MM-dd.log" —
/// сортируется как строка в хронологическом порядке.
/// </summary>
internal static class DutyErrorLogPaths
{
    private const string DirectoryName = "logs_duty";
    private const string FilePrefix = "error-";
    private const string FileExtension = ".log";
    private const string DateFormat = "yyyy-MM-dd";

    public static string GetLogDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, DirectoryName);
    }

    public static string GetLogFilePath(DateTime utcDate)
    {
        var fileName = $"{FilePrefix}{utcDate.ToString(DateFormat, CultureInfo.InvariantCulture)}{FileExtension}";
        return Path.Combine(GetLogDirectory(), fileName);
    }

    /// <summary>
    /// Удаляет файлы логов старше <paramref name="retentionDays"/> дней, чтобы каталог не рос
    /// бесконечно. Вызывается один раз при старте сервера (не по расписанию) — рестарты в этом
    /// форке и так частые (см. runQuickServer.bat), доп. поток для этого не нужен. Ошибки
    /// удаления тихо игнорируются — это уборка, а не критичная для работы сервера операция.
    /// </summary>
    public static void CleanupOldLogs(int retentionDays)
    {
        var directory = GetLogDirectory();
        if (!Directory.Exists(directory))
            return;

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);

        foreach (var path in Directory.EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(FilePrefix, StringComparison.Ordinal))
                continue;

            var datePart = name[FilePrefix.Length..];
            if (!DateTime.TryParseExact(datePart, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                continue;

            if (fileDate >= cutoff)
                continue;

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Файл занят/недоступен — не критично, попробуем в следующий раз.
            }
            catch (UnauthorizedAccessException)
            {
                // Аналогично.
            }
        }
    }
}
