using System;
using System.Text.RegularExpressions;

namespace UNVRC.PerfectSync.Editor
{

internal static class PerfectSyncSearch
{
	public static bool Matches(string value, string search, PerfectSyncMatchMode matchMode, out string regexError)
	{
		regexError = "";
		if (string.IsNullOrWhiteSpace(search))
		{
			return true;
		}

		if (matchMode == PerfectSyncMatchMode.Regex)
		{
			try
			{
				return Regex.IsMatch(value, search, RegexOptions.IgnoreCase);
			}
			catch (ArgumentException exception)
			{
				regexError = exception.Message;
				return false;
			}
		}

		var normalizedValue = value.ToLowerInvariant();
		var normalizedSearch = search.ToLowerInvariant();
		if (normalizedSearch.Contains("*"))
		{
			var pattern = "^" + Regex.Escape(normalizedSearch).Replace("\\*", ".*") + "$";
			return Regex.IsMatch(normalizedValue, pattern);
		}

		return matchMode switch
		{
			PerfectSyncMatchMode.Contains => normalizedValue.Contains(normalizedSearch),
			PerfectSyncMatchMode.StartsWith => normalizedValue.StartsWith(normalizedSearch, StringComparison.Ordinal),
			PerfectSyncMatchMode.EndsWith => normalizedValue.EndsWith(normalizedSearch, StringComparison.Ordinal),
			PerfectSyncMatchMode.Exact => normalizedValue == normalizedSearch,
			_ => false,
		};
	}
}
}
