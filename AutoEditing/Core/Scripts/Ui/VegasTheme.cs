using System.Drawing;
using System.Windows.Forms;

namespace Core.Scripts.Ui
{
    /// <summary>
    /// Color palette and control styling that matches the VEGAS Pro 20 dark UI theme.
    /// </summary>
    /// <remarks>
    /// VEGAS Pro 20 renders its docked windows with a dark gray surface, flat controls,
    /// subtle 1px borders and a blue accent for primary actions. Apply these helpers to
    /// standard WinForms controls so the script UI blends in with the host application.
    /// </remarks>
    public static class VegasTheme
    {
        // Surfaces
        public static readonly Color WindowBackground = Color.FromArgb(45, 45, 45);
        public static readonly Color InputBackground = Color.FromArgb(30, 30, 30);
        public static readonly Color LogBackground = Color.FromArgb(24, 24, 24);

        // Controls
        public static readonly Color ControlBackground = Color.FromArgb(62, 62, 62);
        public static readonly Color ControlHover = Color.FromArgb(78, 78, 78);
        public static readonly Color ControlPressed = Color.FromArgb(94, 94, 94);

        // Borders
        public static readonly Color BorderColor = Color.FromArgb(24, 24, 24);
        public static readonly Color SubtleBorder = Color.FromArgb(70, 70, 70);

        // Text
        public static readonly Color TextColor = Color.FromArgb(219, 219, 219);
        public static readonly Color DimTextColor = Color.FromArgb(152, 152, 152);
        public static readonly Color LogTextColor = Color.FromArgb(200, 200, 200);

        // Accent (VEGAS selection blue)
        public static readonly Color Accent = Color.FromArgb(70, 138, 207);
        public static readonly Color AccentHover = Color.FromArgb(88, 155, 222);
        public static readonly Color AccentPressed = Color.FromArgb(56, 116, 178);
        public static readonly Color AccentBorder = Color.FromArgb(38, 88, 138);

        public static Font BaseFont
        {
            get { return new Font("Segoe UI", 9F); }
        }

        public static Font SectionHeaderFont
        {
            get { return new Font("Segoe UI", 8F, FontStyle.Bold); }
        }

        public static Font MonospaceFont
        {
            get { return new Font("Consolas", 9F); }
        }

        /// <summary>
        /// Styles a button as a flat, dark VEGAS-style secondary button.
        /// </summary>
        public static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ControlBackground;
            button.ForeColor = TextColor;
            button.Font = BaseFont;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = ControlHover;
            button.FlatAppearance.MouseDownBackColor = ControlPressed;
        }

        /// <summary>
        /// Styles a button as the accented primary action button.
        /// </summary>
        public static void StylePrimaryButton(Button button)
        {
            StyleButton(button);
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = AccentBorder;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentPressed;
        }

        /// <summary>
        /// Styles a text box as a dark, flat-bordered VEGAS-style input field.
        /// </summary>
        public static void StyleTextBox(TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = InputBackground;
            textBox.ForeColor = TextColor;
            textBox.Font = BaseFont;
        }

        /// <summary>
        /// Styles a check box to match the flat dark theme.
        /// </summary>
        public static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = TextColor;
            checkBox.Font = BaseFont;
            checkBox.Cursor = Cursors.Hand;
        }

        /// <summary>
        /// Creates an uppercase, dimmed section header label like the ones used in
        /// VEGAS Pro docked panes.
        /// </summary>
        public static Label CreateSectionHeader(string text)
        {
            Label header = new Label
            {
                Text = text.ToUpperInvariant(),
                Font = SectionHeaderFont,
                ForeColor = DimTextColor,
                BackColor = Color.Transparent,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 4)
            };
            return header;
        }

        /// <summary>
        /// Creates a standard field label.
        /// </summary>
        public static Label CreateFieldLabel(string text)
        {
            Label label = new Label
            {
                Text = text,
                Font = BaseFont,
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };
            return label;
        }
    }
}
