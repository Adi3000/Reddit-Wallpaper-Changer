using System;
using System.Drawing;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer
{
    public class TabSelector
    {
        private readonly Color _defaultColor = Color.White;
        private readonly Color _backColor = Color.FromArgb(214, 234, 244);
        private readonly Color _borderColor = Color.FromArgb(130, 195, 228);

        Panel _selectedPanel;
        Button _selectedButton;

        public TabSelector(Panel initialSelectedPanel, Button initialSelectedButton)
        {
            _selectedPanel = initialSelectedPanel ?? throw new ArgumentNullException(nameof(initialSelectedPanel));
            _selectedButton = initialSelectedButton ?? throw new ArgumentNullException(nameof(initialSelectedButton));
        }

        public void Select(Panel panel, Button button)
        {
            if (panel == null)
                throw new ArgumentNullException(nameof(panel));
            if (button == null)
                throw new ArgumentNullException(nameof(button));

            if (_selectedPanel == panel) return;

            _selectedPanel.Visible = false;
            panel.Visible = true;

            _selectedPanel = panel;

            _selectedButton.BackColor = _defaultColor;
            _selectedButton.FlatAppearance.BorderColor = _defaultColor;
            _selectedButton.FlatAppearance.MouseDownBackColor = _defaultColor;
            _selectedButton.FlatAppearance.MouseOverBackColor = _defaultColor;

            button.BackColor = _backColor;
            button.FlatAppearance.BorderColor = _borderColor;
            button.FlatAppearance.MouseDownBackColor = _backColor;
            button.FlatAppearance.MouseOverBackColor = _backColor;

            _selectedButton = button;
        }
    }
}
