using System.Text.RegularExpressions;

namespace Content.Shared.ADT.TenCodes;

public static class ADTTenCodes
{
    public const string MarkupTag = "tencode";
    public const string Prefix = "10-";

    // _Duty-start: коды 11-xx (11-41 медик, 11-99 офицер в опасности) — их шлёт нейроинтерфейс NT-SEC
    public const string DutyPrefix11 = "11-";

    public static readonly Regex CodeRegex = new(@"\b1[01]-\d{1,3}\b", RegexOptions.Compiled);
    // _Duty-end
}
