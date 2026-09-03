using MudBlazor;

namespace Needly.Web.Components.Theme;

internal static class NeedlyTheme
{
    internal static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0969DA",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#59636E",
            SecondaryContrastText = "#FFFFFF",
            Background = "#F6F8FA",
            Surface = "#FFFFFF",
            AppbarBackground = "#F6F8FA",
            AppbarText = "#1F2328",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1F2328",
            TextPrimary = "#1F2328",
            TextSecondary = "#59636E",
            ActionDefault = "#59636E",
            Divider = "#D1D9E0",
            LinesDefault = "#D1D9E0",
            Success = "#1A7F37",
            Warning = "#9A6700",
            Error = "#CF222E",
            Info = "#0969DA"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#2F81F7",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#8B949E",
            SecondaryContrastText = "#FFFFFF",
            Background = "#0D1117",
            Surface = "#161B22",
            AppbarBackground = "#010409",
            AppbarText = "#F0F6FC",
            DrawerBackground = "#0D1117",
            DrawerText = "#F0F6FC",
            TextPrimary = "#F0F6FC",
            TextSecondary = "#8B949E",
            ActionDefault = "#8B949E",
            Divider = "#30363D",
            LinesDefault = "#30363D",
            Success = "#3FB950",
            Warning = "#D29922",
            Error = "#F85149",
            Info = "#58A6FF"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "0.875rem",
                FontWeight = "400",
                LineHeight = "1.45",
                LetterSpacing = "0"
            },
            H1 = new H1Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "2rem",
                FontWeight = "600",
                LineHeight = "1.25",
                LetterSpacing = "0"
            },
            H5 = new H5Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "1rem",
                FontWeight = "600",
                LineHeight = "1.5",
                LetterSpacing = "0"
            },
            H6 = new H6Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "1rem",
                FontWeight = "600",
                LineHeight = "1.5",
                LetterSpacing = "0"
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "0.8125rem",
                FontWeight = "600",
                LineHeight = "1.5",
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