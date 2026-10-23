using System;
using System.Text;

namespace BMS.Application.Utilities;

/// <summary>
/// Utility converting numeric currency amounts into standard Vietnamese words.
/// Conforms to: Ministry of Finance Accounting Regulations & Decree 123/2020/ND-CP.
/// </summary>
public static class VietnameseCurrencyWords
{
    private static readonly string[] Digits = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
    private static readonly string[] Units = { "", "nghìn", "triệu", "tỉ", "nghìn tỉ", "triệu tỉ" };

    public static string Convert(decimal amount)
    {
        long integral = (long)Math.Round(amount, MidpointRounding.AwayFromZero);
        if (integral <= 0)
        {
            return "Không đồng chẵn";
        }

        string sNumber = integral.ToString();
        // Pad with zeros to multiple of 3
        while (sNumber.Length % 3 != 0)
        {
            sNumber = "0" + sNumber;
        }

        int groupCount = sNumber.Length / 3;
        var result = new StringBuilder();

        for (int i = 0; i < groupCount; i++)
        {
            int groupIndex = groupCount - 1 - i;
            string groupStr = sNumber.Substring(i * 3, 3);
            int groupVal = int.Parse(groupStr);

            if (groupVal > 0 || (groupIndex == 0 && result.Length == 0))
            {
                string groupWord = ReadThreeDigits(groupStr, i == 0);
                if (!string.IsNullOrWhiteSpace(groupWord))
                {
                    result.Append(groupWord);
                    if (groupIndex > 0 && groupIndex < Units.Length)
                    {
                        result.Append(" ").Append(Units[groupIndex]);
                    }
                    result.Append(" ");
                }
            }
        }

        string rawText = result.ToString().Trim();
        if (string.IsNullOrEmpty(rawText))
        {
            return "Không đồng chẵn";
        }

        // Clean up redundant spaces
        rawText = System.Text.RegularExpressions.Regex.Replace(rawText, @"\s+", " ");

        // Capitalize first letter and append standard suffix
        string formatted = char.ToUpper(rawText[0]) + rawText.Substring(1) + " đồng chẵn";
        return formatted;
    }

    private static string ReadThreeDigits(string groupStr, bool isFirstGroup)
    {
        int hundreds = groupStr[0] - '0';
        int tens = groupStr[1] - '0';
        int ones = groupStr[2] - '0';

        var sb = new StringBuilder();

        if (hundreds > 0 || !isFirstGroup)
        {
            sb.Append(Digits[hundreds]).Append(" trăm");
        }

        if (tens > 1)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append(Digits[tens]).Append(" mươi");
            if (ones == 1)
            {
                sb.Append(" mốt");
            }
            else if (ones == 5)
            {
                sb.Append(" lăm");
            }
            else if (ones > 0)
            {
                sb.Append(" ").Append(Digits[ones]);
            }
        }
        else if (tens == 1)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append("mười");
            if (ones == 1)
            {
                sb.Append(" một");
            }
            else if (ones == 5)
            {
                sb.Append(" lăm");
            }
            else if (ones > 0)
            {
                sb.Append(" ").Append(Digits[ones]);
            }
        }
        else // tens == 0
        {
            if (ones > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" lẻ ").Append(Digits[ones]);
                }
                else
                {
                    sb.Append(Digits[ones]);
                }
            }
        }

        return sb.ToString().Trim();
    }
}
