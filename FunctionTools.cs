using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace dwmuller.HomeNet
{
    static class FunctionTools
    {
        public static bool? GetBoolParam(HttpRequest req, string key, dynamic reqBody = null)
        {
            bool? result = TryStringToBool(req.Query[key]);
            if (!result.HasValue && !reqBody is null)
            {
                result = reqBody[key];
            }
            return result;
        }

        public static string GetStringParam(HttpRequest req, string key, dynamic reqBody = null)
        {
            string result = req.Query[key];
            if (string.IsNullOrEmpty(result) && !reqBody is null)
            {
                result = reqBody[key];
            }
            return result;
        }

        private static bool? TryStringToBool(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            if (Regex.Match(value, @"^((t(rue)?)|(y(es)?)|1)$", RegexOptions.IgnoreCase).Success)
                return true;
            if (Regex.Match(value, @"^((f(alse)?)|(n(o)?)|0)$", RegexOptions.IgnoreCase).Success)
                return false;
            throw new ArgumentOutOfRangeException(nameof(value), value, "Not interpretable as a Boolean value.");
        }
    }
}