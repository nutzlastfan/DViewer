using System;
using Microsoft.Maui.Controls;

namespace DViewer.Behaviors
{
    // --- DatePicker: commit bei DateSelected ---
    public sealed class DatePickerCommitBehavior : Behavior<DatePicker>
    {
        public static readonly BindableProperty TargetProperty =
            BindableProperty.Create(nameof(Target), typeof(DateTime?), typeof(DatePickerCommitBehavior), null, BindingMode.TwoWay);

        public DateTime? Target
        {
            get => (DateTime?)GetValue(TargetProperty);
            set => SetValue(TargetProperty, value);
        }

        DatePicker? _picker;

        protected override void OnAttachedTo(DatePicker bindable)
        {
            base.OnAttachedTo(bindable);
            _picker = bindable;
            _picker.DateSelected += OnDateSelected;
        }

        protected override void OnDetachingFrom(DatePicker bindable)
        {
            base.OnDetachingFrom(bindable);
            if (_picker != null)
                _picker.DateSelected -= OnDateSelected;
            _picker = null;
        }

        void OnDateSelected(object? sender, DateChangedEventArgs e)
        {
            // Nur user-initiierte Wahl wird hier gefeuert
            Target = e.NewDate;
        }
    }

    // --- TimePicker: commit beim Unfocus (Popup zu) ---
    public sealed class TimePickerCommitBehavior : Behavior<TimePicker>
    {
        public static readonly BindableProperty TargetProperty =
            BindableProperty.Create(nameof(Target), typeof(TimeSpan?), typeof(TimePickerCommitBehavior), null, BindingMode.TwoWay);

        public TimeSpan? Target
        {
            get => (TimeSpan?)GetValue(TargetProperty);
            set => SetValue(TargetProperty, value);
        }

        TimePicker? _picker;

        protected override void OnAttachedTo(TimePicker bindable)
        {
            base.OnAttachedTo(bindable);
            _picker = bindable;
            _picker.Unfocused += OnUnfocused;
        }

        protected override void OnDetachingFrom(TimePicker bindable)
        {
            base.OnDetachingFrom(bindable);
            if (_picker != null)
                _picker.Unfocused -= OnUnfocused;
            _picker = null;
        }

        void OnUnfocused(object? sender, FocusEventArgs e)
        {
            if (_picker == null) return;
            Target = _picker.Time;
        }
    }

    // --- Picker (Sex): commit bei Auswahländerung, ignoriert Initialisierung ---
    public sealed class PickerCommitBehavior : Behavior<Picker>
    {
        public static readonly BindableProperty TargetProperty =
            BindableProperty.Create(nameof(Target), typeof(string), typeof(PickerCommitBehavior), default(string), BindingMode.TwoWay);

        public string Target
        {
            get => (string)GetValue(TargetProperty);
            set => SetValue(TargetProperty, value);
        }

        Picker? _picker;
        bool _initializing = true;

        protected override void OnAttachedTo(Picker bindable)
        {
            base.OnAttachedTo(bindable);
            _picker = bindable;
            _picker.SelectedIndexChanged += OnSelectedIndexChanged;

            // nach einem UI-Tick Initialisierung beenden (damit ein evtl. Setzen des Default-Index NICHT zurückschreibt)
            Device.BeginInvokeOnMainThread(() => _initializing = false);
        }

        protected override void OnDetachingFrom(Picker bindable)
        {
            base.OnDetachingFrom(bindable);
            if (_picker != null)
                _picker.SelectedIndexChanged -= OnSelectedIndexChanged;
            _picker = null;
        }

        void OnSelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_picker == null) return;
            if (_initializing) return; // default/initiale Auswahl ignorieren

            if (_picker.SelectedItem is string s)
                Target = s;
            else if (_picker.SelectedIndex < 0)
                Target = string.Empty;
        }
    }
}
