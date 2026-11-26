using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ToolboxApp.Utils
{
    public partial class WpfRegionSelectWindow : Window
    {
        private Point _start, _current;
        private bool _selecting;

        public System.Drawing.Rectangle SelectionPhysical { get; private set; }
        public bool IsConfirmed { get; private set; }

        public WpfRegionSelectWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { IsConfirmed = false; Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _selecting = true;
                _start = _current = e.GetPosition(this);
                UpdateSelection();
            }
            else if (e.RightButton == MouseButtonState.Pressed)
            { IsConfirmed = false; Close(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_selecting) { _current = e.GetPosition(this); UpdateSelection(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Released || !_selecting)
            { base.OnMouseUp(e); return; }
            _selecting = false;
            var r = GetSelectionRect();
            if (r.Width < 2 || r.Height < 2)
            { IsConfirmed = false; Close(); return; }
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double s = dpi.DpiScaleX;
            SelectionPhysical = new System.Drawing.Rectangle(
                (int)(r.X * s), (int)(r.Y * s),
                (int)(r.Width * s), (int)(r.Height * s));
            IsConfirmed = true;
            Close();
        }

        private Rect GetSelectionRect()
        {
            double x = Math.Min(_start.X, _current.X), y = Math.Min(_start.Y, _current.Y);
            return new Rect(x, y, Math.Abs(_start.X - _current.X), Math.Abs(_start.Y - _current.Y));
        }

        private void UpdateSelection()
        {
            var r = GetSelectionRect();
            if (r.Width > 0 && r.Height > 0)
            {
                SelRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelRect, r.X); Canvas.SetTop(SelRect, r.Y);
                SelRect.Width = r.Width; SelRect.Height = r.Height;
                SizeText.Text = $"{(int)r.Width} x {(int)r.Height}";
                SizeLabel.Visibility = Visibility.Visible;
                double lx = r.X + 4, ly = r.Y - SizeLabel.ActualHeight - 2;
                if (ly < 0) ly = r.Y + 4;
                Canvas.SetLeft(SizeLabel, lx); Canvas.SetTop(SizeLabel, ly);
            }
            else { SelRect.Visibility = Visibility.Collapsed; SizeLabel.Visibility = Visibility.Collapsed; }
        }
    }
}
