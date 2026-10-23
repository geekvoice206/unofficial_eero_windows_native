using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace EeroDesktop
{
    public static class PhoneInput
    {
        public static string Normalize(string input)
        {
            string text = (input ?? "").Trim();
            if (!Regex.IsMatch(text, @"^\+?[0-9\s().-]+$"))
                throw new ArgumentException("Enter a phone number, or choose Email address to sign in with email.");
            string digits = new string(text.Where(c => c >= '0' && c <= '9').ToArray());
            bool international = text.StartsWith("+");
            if (!international)
            {
                if (digits.Length == 10) digits = "1" + digits;
                else if (digits.Length != 11 || !digits.StartsWith("1"))
                    throw new ArgumentException("Enter a 10-digit US/Canada number, or include + and your country code.");
            }
            if (digits.Length < 8 || digits.Length > 15 || digits[0] == '0')
                throw new ArgumentException("Enter a complete phone number with a valid country code (up to 15 digits).");
            if (digits.StartsWith("1") && digits.Length != 11)
                throw new ArgumentException("US/Canada numbers need 10 digits after +1.");
            return "+" + digits;
        }

        // Format complete numbers only, so backspacing and partially typed numbers stay editable.
        public static string FormatForDisplay(string input)
        {
            string normalized;
            try { normalized = Normalize(input); }
            catch (ArgumentException) { return input ?? ""; }
            if (normalized.StartsWith("+1") && normalized.Length == 12)
                return "+1 (" + normalized.Substring(2, 3) + ") " + normalized.Substring(5, 3) + "-" + normalized.Substring(8, 4);
            return normalized;
        }

        public static int PreserveCaret(string before, int caret, string after)
        {
            int followingDigits = before.Substring(Math.Min(caret, before.Length)).Count(c => c >= '0' && c <= '9');
            if (followingDigits == 0) return after.Length;
            for (int i = after.Length - 1; i >= 0; i--)
                if (after[i] >= '0' && after[i] <= '9' && --followingDigits == 0) return i;
            return 0;
        }
    }
}
