using MudBlazor;

namespace Needly.Web.Components.Theme;

internal static class NeedlyTheme
{
    internal static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0B4DFF",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#E64B40",
            SecondaryContrastText = "#FFFFFF",
            Background = "#F2F4F8",
            Surface = "#FCFDFE",
            AppbarBackground = "#FCFDFE",
            AppbarText = "#111827",
            DrawerBackground = "#081126",
            DrawerText = "#E8EDFF",
            TextPrimary = "#111827",
            TextSecondary = "#5C667A",
            ActionDefault = "#455168",
            Divider = "#C8D0E0",
            LinesDefault = "#C8D0E0",
            Success = "#07865F",
            Warning = "#C56A08",
            Error = "#D43F3A",
            Info = "#1167C4"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7192FF",
            PrimaryContrastText = "#071029",
            Secondary = "#FF8178",
            SecondaryContrastText = "#2B0D0A",
            Background = "#090E19",
            Surface = "#111827",
            AppbarBackground = "#0D1424",
            AppbarText = "#F3F6FF",
            DrawerBackground = "#050A15",
            DrawerText = "#DDE5FF",
            TextPrimary = "#F3F6FF",
            TextSecondary = "#A9B4CB",
            ActionDefault = "#B9C3D8",
            Divider = "#303B52",
            LinesDefault = "#303B52",
            Success = "#49C593",
            Warning = "#F4AE55",
            Error = "#FF716B",
            Info = "#6CB4F5"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "0.9375rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "0"
            },
            H1 = new H1Typography
            {
                FontFamily = ["Syne", "Arial Black", "sans-serif"],
                FontSize = "2.75rem",
                FontWeight = "700",
                LineHeight = "1.02",
                LetterSpacing = "0"
            },
            H5 = new H5Typography
            {
                FontFamily = ["Syne", "Arial Black", "sans-serif"],
                FontSize = "1.35rem",
                FontWeight = "700",
                LineHeight = "1.15",
                LetterSpacing = "0"
            },
            H6 = new H6Typography
            {
                FontFamily = ["Syne", "Arial Black", "sans-serif"],
                FontSize = "1.1rem",
                FontWeight = "600",
                LineHeight = "1.25",
                LetterSpacing = "0"
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = ["IBM Plex Mono", "Consolas", "monospace"],
                FontSize = "0.75rem",
                FontWeight = "600",
                LineHeight = "1.4",
                LetterSpacing = "0"
            },
            Button = new ButtonTypography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "0.875rem",
                FontWeight = "600",
                LineHeight = "1.4",
                LetterSpacing = "0",
                TextTransform = "none"
            }
        }
    };
}