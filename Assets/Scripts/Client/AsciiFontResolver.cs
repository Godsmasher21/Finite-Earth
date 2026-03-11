using UnityEngine;

public static class AsciiFontResolver
{
    private static Font cachedFont;

    public static Font ResolveFont(int fallbackSize = 16)
    {
        if (cachedFont != null)
        {
            return cachedFont;
        }

        cachedFont = Resources.Load<Font>("Fonts/VT323-Regular");
        if (cachedFont != null)
        {
            return cachedFont;
        }

        cachedFont = Resources.Load<Font>("Fonts/PressStart2P-Regular");
        if (cachedFont != null)
        {
            return cachedFont;
        }

        cachedFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Consolas", "JetBrains Mono", "Courier New", "Lucida Console" },
            Mathf.Clamp(fallbackSize, 10, 40));
        if (cachedFont != null)
        {
            return cachedFont;
        }

        cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return cachedFont;
    }
}
