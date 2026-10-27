using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.Bot
{
    // FMEconomyManager：每一步保留Java截断，最后的clean number实际是整数除法截断。
    public sealed class MarketEconomy
    {
        readonly Random random;
        readonly Func<DateTime> localTime;
        List<double> indices;
        public MarketEconomy(Random random = null, Func<DateTime> localTime = null)
        { this.random = random ?? new Random(); this.localTime = localTime ?? (() => DateTime.Now); }
        public static int JavaInt(double value) => double.IsNaN(value) ? 0 : value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
        public static bool IsHotRoom(int mapId) => mapId == 910000001 || mapId == 910000002 || mapId == 910000007;
        public static int RoomPremium(int mapId, int price)
        {
            double factor;
            switch (mapId)
            {
                case 910000001: factor = 1.25; break;
                case 910000002: factor = 1.20; break;
                case 910000003: factor = 1.10; break;
                case 910000007: factor = 1.15; break;
                case 910000014: factor = .97; break;
                case 910000015: factor = .95; break;
                case 910000016: factor = .93; break;
                case 910000017: factor = .91; break;
                case 910000018: factor = .90; break;
                case 910000019: factor = .88; break;
                case 910000020: factor = .86; break;
                case 910000021: factor = .84; break;
                case 910000022: factor = .82; break;
                default: factor = 1; break;
            }
            return JavaInt(price * factor);
        }
        public static int DoorPremium(int mapId, int x, int y, int price)
        {
            if (mapId >= 910000001 && mapId <= 910000006 && x >= 300 && x <= 640 && y >= -80 && y <= 34 ||
                mapId >= 910000007 && mapId <= 910000012 && x >= -709 && x <= -109 && y >= -100 && y <= 102)
                return JavaInt(price * 1.2);
            return price;
        }
        public static int DayPrice(int price, DayOfWeek day)
            => JavaInt(price * (day == DayOfWeek.Friday || day == DayOfWeek.Saturday || day == DayOfWeek.Sunday ? 1.10 :
                day == DayOfWeek.Monday || day == DayOfWeek.Tuesday ? .95 : 1));
        public IReadOnlyList<double> MarketIndices
        {
            get
            {
                if (indices == null)
                {
                    indices = new List<double>(12);
                    for (int i = 0; i < 12; i++) indices.Add(Math.Max(.85, Math.Min(1.15,
                        (.85 + 1.15) / 2 + (1.15 - .85) / 2 * Math.Sin(2 * Math.PI / 12 * i) + (random.NextDouble() - .5) * .05)));
                }
                return indices;
            }
        }
        public double CurrentIndex => MarketIndices[(localTime().Hour / 2) % 12];
        public int RandomAdjustment(int price)
        {
            int roll = random.Next(100) + 1;
            double factor = roll <= 10 ? .9 : roll <= 30 ? .95 : roll <= 70 ? 1 : roll <= 90 ? 1.05 : 1.1;
            return JavaInt(price * factor);
        }
        public int Price(int price, int? mapId = null, int? x = null, int? y = null)
        {
            price = JavaInt(price * CurrentIndex);
            price = DayPrice(price, localTime().DayOfWeek);
            price = RandomAdjustment(price);
            if (mapId.HasValue) price = RoomPremium(mapId.Value, price);
            if (mapId.HasValue && x.HasValue && y.HasValue) price = DoorPremium(mapId.Value, x.Value, y.Value, price);
            return Style(price);
        }
        public static int Quantity(int quantity, int mapId, int x, int y)
            => quantity == 1 ? 1 : DoorPremium(mapId, x, y, RoomPremium(mapId, quantity));
        public static int CleanNumber(int number)
        {
            int absolute = number == int.MinValue ? int.MinValue : Math.Abs(number);
            int digits = absolute.ToString(CultureInfo.InvariantCulture).Length;
            int significant = digits >= 6 && digits <= 9 ? 2 : digits == 10 ? 3 : 0;
            if (significant == 0) return number;
            // Java源码先parse前三/两位（未使用）再number/orderMagnitude，两者都是int。
            int.Parse(number.ToString(CultureInfo.InvariantCulture).Substring(0, significant), CultureInfo.InvariantCulture);
            int magnitude = (int)Math.Pow(10, digits - significant);
            return unchecked((number / magnitude) * magnitude);
        }
        public static int TrailingDigit(int number)
        {
            char[] digits = number.ToString(CultureInfo.InvariantCulture).ToCharArray(); int last = digits.Length - 1;
            while (last >= 0 && digits[last] == '0') last--;
            if (last < 0) return number;
            for (int i = last + 1; i < digits.Length; i++) digits[i] = digits[last];
            return int.Parse(new string(digits), CultureInfo.InvariantCulture);
        }
        public int Style(int number)
        {
            int style = random.Next(3); number = CleanNumber(number);
            return style == 1 ? unchecked(number - 1) : style == 2 ? TrailingDigit(number) : number;
        }
        public static string Shorthand(int price, int decimals = 1)
        {
            if (price < 1000) return price.ToString(CultureInfo.InvariantCulture);
            string[] suffix = { "", "k", "m", "b" }; int index = 0; double amount = price;
            while (amount >= 1000 && index < 3) { amount /= 1000; index++; }
            if (decimals <= 0) return Math.Floor(amount + .5).ToString(CultureInfo.InvariantCulture) + suffix[index];
            return decimal.Round((decimal)amount, decimals, MidpointRounding.AwayFromZero)
                .ToString("F" + decimals, CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') + suffix[index];
        }
        public string TierForRoom(int mapId)
        {
            int s, a;
            if (IsHotRoom(mapId)) { s = 85; a = 13; }
            else if (mapId == 910000003) { s = 60; a = 35; }
            else if (mapId >= 910000004 && mapId <= 910000006 || mapId >= 910000008 && mapId <= 910000012) { s = 25; a = 60; }
            else if (mapId == 910000013) { s = 33; a = 33; }
            else if (mapId >= 910000014 && mapId <= 910000017) { s = 15; a = 30; }
            else if (mapId >= 910000018 && mapId <= 910000022) { s = 20; a = 40; }
            else { s = 25; a = 55; }
            int roll = random.Next(100);
            return roll < s ? "S" : roll < s + a ? "A" : "B";
        }
        public static int WzPriceByJob(int price, int jobStyle)
        {
            int adjusted = JavaInt(price * 1.5);
            return JavaInt(adjusted * (jobStyle == 100 ? 1.4 : jobStyle == 200 ? 1.35 : jobStyle == 300 ? 1.30 : jobStyle == 400 ? 1.5 : jobStyle == 500 ? 1.25 : 1.30));
        }
    }
}
