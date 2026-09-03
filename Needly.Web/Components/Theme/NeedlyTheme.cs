using MudBlazor;

namespace Needly.Web.Components.Theme;

internal static class NeedlyTheme
{
    internal static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#176B60",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#9B6328",
            SecondaryContrastText = "#FFFFFF",
            Background = "#F4F3EE",
            Surface = "#FFFEF9",
            AppbarBackground = "#FFFEF9",
            AppbarText = "#202723",
            DrawerBackground = "#F4F3EE",
            DrawerText = "#33413B",
            TextPrimary = "#202723",
            TextSecondary = "#65716B",
            ActionDefault = "#53635C",
            Divider = "#D9DDD5",
            LinesDefault = "#D9DDD5",
            Success = "#2D7758",
            Warning = "#A66A16",
            Error = "#B5473C",
            Info = "#3D6F86"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7CC3B5",
            PrimaryContrastText = "#102B27",
            Secondary = "#E0A86E",
            SecondaryContrastText = "#31200F",
            Background = "#1C1D1A",
            Surface = "#252722",
            AppbarBackground = "#21231F",
            AppbarText = "#EFF1EA",
            DrawerBackground = "#1F211D",
            DrawerText = "#D6DDD5",
            TextPrimary = "#EFF1EA",
            TextSecondary = "#AFB8AF",
            ActionDefault = "#B9C1B8",
            Divider = "#3D413A",
            LinesDefault = "#3D413A",
            Success = "#72B892",
            Warning = "#E0AC60",
            Error = "#E08479",
            Info = "#79AFC5"
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
                FontFamily = ["Newsreader", "Georgia", "serif"],
                FontSize = "2.5rem",
                FontWeight = "600",
                LineHeight = "1.08",
                LetterSpacing = "0"
            },
            H5 = new H5Typography
            {
                FontFamily = ["Newsreader", "Georgia", "serif"],
                FontSize = "1.45rem",
                FontWeight = "600",
                LineHeight = "1.2",
                LetterSpacing = "0"
            },
            H6 = new H6Typography
            {
                FontFamily = ["Newsreader", "Georgia", "serif"],
                FontSize = "1.25rem",
                FontWeight = "600",
                LineHeight = "1.2",
                LetterSpacing = "0"
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "sans-serif"],
                FontSize = "0.8125rem",
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