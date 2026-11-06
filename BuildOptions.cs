using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LinkArea2024
{
    internal sealed class RevitWindow : IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public RevitWindow(IntPtr handle) { Handle = handle; }
    }

    internal sealed class BuildOptions : Form
    {
        private readonly CheckBox placeTag;
        private readonly NumericUpDown margin;
        private readonly CheckBox includeNested;
        private readonly CheckBox fillSmr;
        private readonly TextBox block, section, description;
        private readonly ComboBox prefix;
        public SmrValues Smr { get; private set; }
        private readonly CheckBox simpleContour;
        public bool SimpleContour { get { return simpleContour.Checked; } }
        public bool PlaceTag { get { return placeTag.Checked; } }
        public double MarginMm { get { return (double)margin.Value; } }
        public bool IncludeNested { get { return includeNested.Checked; } }

        public BuildOptions(double hostElevation, string hostLevelName, string linkName)
        {
            Text = "LinkArea — создание зоны и параметры СМР";
            ClientSize = new Size(840, 605);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Controls.Add(new Label
            {
                Text = "Связь: " + linkName + "\r\nПлан размещения: " + hostLevelName +
                    "  |  отметка: " + (hostElevation * .3048).ToString("0.000", CultureInfo.CurrentCulture) + " м",
                Location = new Point(16, 14), Size = new Size(808, 56), AutoEllipsis = true
            });
            Controls.Add(new Label
            {
                Text = "Одна зона по физическим телам и сеткам на ВСЕХ этажах выбранной связи.\r\nСлужебные линии и общие габариты семейств не используются. Вложенные связи — по флажку.\r\nВсе стадии и варианты; впадины перекрываются. Контур размещается на активном плане.",
                Location = new Point(16, 82), Size = new Size(808, 76)
            });
            placeTag = new CheckBox
            {
                Text = "Ставить марку зоны", Checked = false,
                Location = new Point(16, 167), Size = new Size(340, 28)
            };
            Controls.Add(placeTag);
            includeNested = new CheckBox
            {
                Text = "Включать загруженные вложенные связи (могут расширить контур на другие корпуса)",
                Checked = false, Location = new Point(16, 202), Size = new Size(808, 28)
            };
            Controls.Add(includeNested);
            Controls.Add(new Label
            {
                Text = "Запас наружу, мм:", Location = new Point(16, 253), Size = new Size(180, 28)
            });
            margin = new NumericUpDown
            {
                Minimum = 0, Maximum = 10000, Value = 20, Increment = 10,
                Location = new Point(200, 250), Size = new Size(110, 30)
            };
            Controls.Add(margin);
            simpleContour = new CheckBox
            {
                Text = "Простой контур СМР — 4 прямые линии", Checked = true,
                Location = new Point(380, 250), Size = new Size(444, 30)
            };
            Controls.Add(simpleContour);
            fillSmr = new CheckBox
            {
                Text = "Заполнить параметры СМР у новой зоны", Checked = true,
                Location = new Point(16, 295), Size = new Size(600, 28)
            };
            Controls.Add(fillSmr);
            Controls.Add(new Label { Text = "Корпус:", Location = new Point(16, 339), Size = new Size(90, 28) });
            block = new TextBox { Location = new Point(110, 335), Size = new Size(250, 30) };
            Controls.Add(block);
            Controls.Add(new Label { Text = "Секция:", Location = new Point(420, 339), Size = new Size(90, 28) });
            section = new TextBox { Location = new Point(514, 335), Size = new Size(310, 30) };
            Controls.Add(section);
            Controls.Add(new Label { Text = "Префикс описания:", Location = new Point(16, 383), Size = new Size(185, 28) });
            prefix = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(210, 379), Size = new Size(260, 30)
            };
            prefix.Items.AddRange(new object[] { "Без префикса", "ОБЩ_", "АР_", "КР_", "ВИС_" });
            prefix.SelectedIndex = 0;
            Controls.Add(prefix);
            Controls.Add(new Label { Text = "ФОП_Описание СМР:", Location = new Point(16, 427), Size = new Size(190, 28) });
            description = new TextBox { ReadOnly = true, Location = new Point(210, 423), Size = new Size(614, 30) };
            Controls.Add(description);
            Controls.Add(new Label
            {
                Text = "Можно вводить 4 и 1 либо «Корпус 4» и «Секция 1».\r\nЗаполняются ФОП_Блок СМР, ФОП_Секция СМР и ФОП_Описание СМР.\r\nПараметры должны уже существовать в проекте. Остальные параметры зоны не заполняются.",
                Location = new Point(16, 469), Size = new Size(808, 76)
            });
            block.TextChanged += (s, e) => UpdateSmrPreview();
            section.TextChanged += (s, e) => UpdateSmrPreview();
            prefix.SelectedIndexChanged += (s, e) => UpdateSmrPreview();
            fillSmr.CheckedChanged += (s, e) => UpdateSmrPreview();
            UpdateSmrPreview();
            var ok = new Button
            {
                Text = "Построить одну зону",
                Location = new Point(514, 559), Size = new Size(182, 32)
            };
            var cancel = new Button
            {
                Text = "Отмена", DialogResult = DialogResult.Cancel,
                Location = new Point(708, 559), Size = new Size(116, 32)
            };
            ok.Click += (s, e) =>
            {
                try
                {
                    Smr = fillSmr.Checked ? SmrValues.Create(block.Text, section.Text, SelectedPrefix()) : null;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Параметры СМР", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private string SelectedPrefix() { return prefix.SelectedIndex <= 0 ? "" : (string)prefix.SelectedItem; }

        private void UpdateSmrPreview()
        {
            block.Enabled = section.Enabled = prefix.Enabled = description.Enabled = fillSmr.Checked;
            if (!fillSmr.Checked) { description.Text = "Параметры СМР не заполняются"; return; }
            try { description.Text = SmrValues.Create(block.Text, section.Text, SelectedPrefix()).Description; }
            catch (ArgumentException) { description.Text = "Введите корпус и секцию"; }
        }
    }
}
