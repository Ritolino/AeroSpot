using System;
using System.Windows.Media;

namespace AeroSpot.Worker.Models
{
    public sealed class SearchResultItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Category { get; set; } = "File";

        public string CategoryDisplay
        {
            get
            {
                switch (Category?.Trim().ToLowerInvariant())
                {
                    case "applicazione":
                    case "application":
                    case "app":
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_App");
                    case "cartella":
                    case "folder":
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_Folder");
                    case "documento":
                    case "document":
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_Document");
                    case "immagine":
                    case "image":
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_Image");
                    case "video":
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_Video");
                    case "calcolatrice":
                    case "calculator":
                        return AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Calcolatrice" : "Calculator";
                    case "conversione":
                    case "conversion":
                        return AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Conversione" : "Conversion";
                    case "invio per copiare":
                    case "enter to copy":
                        return Category;
                    default:
                        return AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_File");
                }
            }
        }
        public string TargetPath { get; set; } = string.Empty;
        public string? CopyText { get; set; }
        public bool IsActionItem { get; set; }
        public string BadgeColor { get; set; } = "#555555";
        public string BadgeForeground { get; set; } = "#DDDDDD";
        public ImageSource? Icon { get; set; }
        public double TitleFontSize => IsActionItem ? 19 : 14;
        public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

        public SearchResultItem() { }

        public SearchResultItem(string title, string subtitle, string category, string targetPath, string badgeColor = "#3E3E42", string badgeForeground = "#CCCCCC", ImageSource? icon = null, string? copyText = null, bool isActionItem = false)
        {
            Title = title;
            Subtitle = subtitle;
            Category = category;
            TargetPath = targetPath;
            BadgeColor = badgeColor;
            BadgeForeground = badgeForeground;
            Icon = icon;
            CopyText = copyText;
            IsActionItem = isActionItem;
        }
    }
}
