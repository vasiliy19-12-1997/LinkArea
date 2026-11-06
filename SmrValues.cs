using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LinkArea2024
{
    public sealed class SmrValues
    {
        public string Block { get; private set; }
        public string Section { get; private set; }
        public string Description { get; private set; }

        public static SmrValues Create(string block, string section, string prefix)
        {
            prefix = (prefix ?? "").Trim();
            if (prefix != "" && prefix != "ОБЩ_" && prefix != "АР_" && prefix != "КР_" && prefix != "ВИС_")
                throw new ArgumentException("Выберите префикс из списка.");
            var result = new SmrValues
            {
                Block = Normalize(block, "Корпус"),
                Section = Normalize(section, "Секция")
            };
            result.Description = prefix + result.Block + "_" + result.Section;
            return result;
        }

        public Dictionary<string, string> Parameters()
        {
            return new Dictionary<string, string>
            {
                { "ФОП_Блок СМР", Block },
                { "ФОП_Секция СМР", Section },
                { "ФОП_Описание СМР", Description }
            };
        }

        private static string Normalize(string text, string label)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase)) text = text.Substring(label.Length).Trim();
            if (text.Length == 0) throw new ArgumentException("Заполните поле «" + label + "».");
            return label + " " + text;
        }
    }
}
