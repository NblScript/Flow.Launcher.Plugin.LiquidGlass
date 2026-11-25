# Exercise the actual horizontal styles emitted by ThemeWriter on WPF.
# Run on Windows in an STA (pwsh -NoProfile -STA -File ...).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'The WPF smoke test requires an STA thread.'
}
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework

$source = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'ThemeWriter.cs') -Raw
$styles = foreach ($key in @('HorizontalThumbStyle', 'HorizontalScrollBarStyle')) {
    $matches = [regex]::Matches($source, '<Style x:Key=""' + $key + '""[\s\S]*?</Style>')
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $key definition."
    }
    $style = $matches[0].Value
    if ($style -match 'BasedOn\s*=') {
        throw "$key must not depend on a host base style."
    }
    # Decode the C# interpolated verbatim string; the color is supplied below.
    $style.Replace('""', '"').Replace('{{', '{').Replace('}}', '}')
}
$styleText = $styles -join [Environment]::NewLine

# Read both actual palette values from ThemeWriter so the smoke test covers
# light and dark without hard-coding a separate copy of the style.
$palette = [regex]::Match($source, 'string\s+thumb\s*=\s*dark\s*\?\s*"(?<dark>#[0-9A-Fa-f]{8})"\s*:\s*"(?<light>#[0-9A-Fa-f]{8})"')
if (-not $palette.Success) {
    throw 'Could not read the light/dark thumb palette from ThemeWriter.'
}

foreach ($hostHasFallback in @($false, $true)) {
    foreach ($appearance in @('dark', 'light')) {
        $color = $palette.Groups[$appearance].Value
        # Model the two host resource layouts: stable has no horizontal keys;
        # dev has default styles in the merged dictionary that themes override.
        $fallback = if ($hostHasFallback) {
@'
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary>
        <Style x:Key="BaseHorizontalThumbStyle" TargetType="{x:Type Thumb}" />
        <Style x:Key="BaseHorizontalScrollBarStyle" TargetType="{x:Type ScrollBar}">
            <Setter Property="Height" Value="17" />
        </Style>
        <Style x:Key="HorizontalThumbStyle" TargetType="{x:Type Thumb}" BasedOn="{StaticResource BaseHorizontalThumbStyle}" />
        <Style x:Key="HorizontalScrollBarStyle" TargetType="{x:Type ScrollBar}" BasedOn="{StaticResource BaseHorizontalScrollBarStyle}" />
    </ResourceDictionary>
</ResourceDictionary.MergedDictionaries>
'@
        } else { '' }

        $xaml = @"
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
$fallback
$($styleText.Replace('{thumb}', $color))
</ResourceDictionary>
"@
        $dictionary = [System.Windows.Markup.XamlReader]::Parse($xaml)
        $bar = [System.Windows.Controls.Primitives.ScrollBar]::new()
        $bar.Resources.MergedDictionaries.Add($dictionary)
        $bar.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $bar.Minimum = 0
        $bar.Maximum = 100
        $bar.ViewportSize = 25
        $bar.Value = 40
        $bar.Style = $dictionary['HorizontalScrollBarStyle']
        $bar.ApplyTemplate() | Out-Null
        $bar.Measure([System.Windows.Size]::new(200, 5))
        $bar.Arrange([System.Windows.Rect]::new(0, 0, 200, 5))
        $bar.UpdateLayout()

        $track = $bar.Template.FindName('PART_Track', $bar)
        if ($track -isnot [System.Windows.Controls.Primitives.Track] -or
            $track.Orientation -ne [System.Windows.Controls.Orientation]::Horizontal -or
            $bar.Height -ne 5 -or $bar.MinHeight -ne 5) {
            throw 'The native horizontal track template or dimensions were lost.'
        }

        $thumb = $track.Thumb
        if ($null -eq $thumb -or $thumb.Style -ne $dictionary['HorizontalThumbStyle'] -or
            $thumb.Focusable -or $thumb.IsTabStop) {
            throw 'The horizontal track did not resolve the Liquid Glass thumb.'
        }
        $thumb.ApplyTemplate() | Out-Null
        $border = [System.Windows.Media.VisualTreeHelper]::GetChild($thumb, 0)
        if ($border -isnot [System.Windows.Controls.Border] -or
            $border.Background.Color.ToString() -ne $color -or
            $border.CornerRadius.TopLeft -ne 3) {
            throw "Incorrect $appearance thumb palette or corner radius."
        }
        if ($track.ActualWidth -le 0 -or $thumb.ActualWidth -le 0 -or
            $thumb.ActualWidth -ge $track.ActualWidth) {
            throw 'The horizontal track or thumb has an invalid layout.'
        }
        $bar.Value = 70
        $bar.UpdateLayout()
        if ([Math]::Abs($track.Value - $bar.Value) -gt 0.001) {
            throw 'The horizontal track no longer follows the scrollbar value.'
        }
        Write-Host "Horizontal WPF smoke test OK: host fallback=$hostHasFallback, appearance=$appearance."
    }
}
