using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using AeroSpot.Core.Localization;

namespace AeroSpot.Worker.Localization
{
    public sealed class StringsViewModel : INotifyPropertyChanged
    {
        public static StringsViewModel Instance { get; } = new StringsViewModel();

        public string this[string key] => LocalizationService.Get(key);

        public event PropertyChangedEventHandler? PropertyChanged;

        private StringsViewModel()
        {
            LocalizationService.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }
    }

    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new Binding($"[{Key}]")
            {
                Source = StringsViewModel.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
