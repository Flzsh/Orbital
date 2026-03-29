using System;
using System.Windows;
using System.Windows.Input;

namespace Orbital;

public partial class ReminderDialog : Window
{
    public string ReminderText => ReminderInput.Text;
    private bool _isPm = true;

    public DateTime AlertTime
    {
        get
        {
            int.TryParse(HourBox.Text, out int h);
            int.TryParse(MinuteBox.Text, out int m);
            h = Math.Clamp(h, 1, 12);
            m = Math.Clamp(m, 0, 59);
            if (_isPm && h != 12) h += 12;
            if (!_isPm && h == 12) h = 0;
            var today = DateTime.Today;
            var dt = today.AddHours(h).AddMinutes(m);
            if (dt < DateTime.Now) dt = dt.AddDays(1);
            return dt;
        }
    }

    public ReminderDialog()
    {
        InitializeComponent();
        ReminderInput.Focus();

        // Default to next full hour
        var next = DateTime.Now.AddHours(1);
        int hr12 = next.Hour % 12;
        if (hr12 == 0) hr12 = 12;
        HourBox.Text = hr12.ToString();
        MinuteBox.Text = "00";
        _isPm = next.Hour >= 12;
        AmPmButton.Content = _isPm ? "PM" : "AM";

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; Close(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void ToggleAmPm_Click(object sender, RoutedEventArgs e)
    {
        _isPm = !_isPm;
        AmPmButton.Content = _isPm ? "PM" : "AM";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
