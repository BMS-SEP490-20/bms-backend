using System;
using System.Collections.Generic;
using System.Text;

namespace BMS.Application.Common;

/// <summary>
/// Utility for reading Vietnamese currency numbers into words (e.g. 150,000 -> "Một trăm năm mươi nghìn đồng chẵn").
/// Conforms to Vietnamese banking and financial receipt standards.
/// </summary>
public static class VietnameseCurrencyReader
{
    private static readonly string[] Digits = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
    private static readonly string[] Units = { "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ" };

    public static string ToVietnameseWords(decimal amount)
    {
        if (amount == 0) return "Không đồng";
        if (amount < 0) return "Âm " + ToVietnameseWords(Math.Abs(amount));

        long integerPart = (long)Math.Floor(amount);
        if (integerPart == 0) return "Không đồng";

        var groups = new List<int>();
        long temp = integerPart;
        while (temp > 0)
        {
            groups.Add((int)(temp % 1000));
            temp /= 1000;
        }

        var result = new StringBuilder();
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            int groupValue = groups[i];
            if (groupValue == 0) continue;

            string groupText = ReadThreeDigits(groupValue, i < groups.Count - 1);
            result.Append(groupText);
            if (i < Units.Length && !string.IsNullOrEmpty(Units[i]))
            {
                result.Append(' ').Append(Units[i]).Append(' ');
            }
        }

        var text = result.ToString().Trim();
        // Normalize whitespace
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        if (string.IsNullOrEmpty(text))
        {
            text = "không";
        }

        // Capitalize first character
        text = char.ToUpper(text[0]) + text[1..] + " đồng chẵn";
        return text;
    }

    private static string ReadThreeDigits(int number, bool readFullHundreds)
    {
        int hundreds = number / 100;
        int tens = (number % 100) / 10;
        int ones = number % 10;

        var sb = new StringBuilder();

        if (hundreds > 0 || readFullHundreds)
        {
            sb.Append(Digits[hundreds]).Append(" trăm ");
        }

        if (tens > 1)
        {
            sb.Append(Digits[tens]).Append(" mươi ");
            if (ones == 1) sb.Append("mốt ");
            else if (ones == 5) sb.Append("lăm ");
            else if (ones > 0) sb.Append(Digits[ones]).Append(' ');
        }
        else if (tens == 1)
        {
            sb.Append("mười ");
            if (ones == 5) sb.Append("lăm ");
            else if (ones > 0) sb.Append(Digits[ones]).Append(' ');
        }
        else if (tens == 0)
        {
            if (ones > 0)
            {
                if (hundreds > 0 || readFullHundreds) sb.Append("lẻ ");
                sb.Append(Digits[ones]).Append(' ');
            }
        }

        return sb.ToString().TrimEnd();
    }
}
