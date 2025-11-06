using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace VSFastBuildVSIX.ToolWindows
{
    public static class TextUtils
    {
        // Text rendering stuff
        private static GlyphTypeface glyphTypeface_ = null;

        private const double FontSize = 12.0;


        public static bool StaticInitialize()
        {
            if(null != glyphTypeface_)
            {
                return true;
            }
            // Font text
            Typeface typeface = new Typeface(new FontFamily("Segoe UI"),
                                            FontStyles.Normal,
                                            FontWeights.Normal,
                                            FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out glyphTypeface_))
            {
                return false;
            }
            return true;
        }

        public static Point ComputeTextSize(string text)
        {
            Point result = new Point();

            for (int charIndex = 0; charIndex < text.Length; charIndex++)
            {
                ushort glyphIndex = glyphTypeface_.CharacterToGlyphMap[text[charIndex]];

                double width = glyphTypeface_.AdvanceWidths[glyphIndex] * FontSize;

                result.Y = Math.Max(glyphTypeface_.AdvanceHeights[glyphIndex] * FontSize, result.Y);

                result.X += width;
            }

            return result;
        }

        public static void DrawText(DrawingContext dc, string text, double x, double y, double maxWidth, bool bEnableDotDotDot, SolidColorBrush colorBrush)
        {
            ushort[] glyphIndexes = null;
            double[] advanceWidths = null;

            ushort[] tempGlyphIndexes = new ushort[text.Length];
            double[] tempAdvanceWidths = new double[text.Length];

            double totalTextWidth = 0;
            double maxHeight = 0.0f;

            bool needDoTDotDot = false;
            double desiredTextWidth = maxWidth;
            int charIndex = 0;

            // Build the text info and measure the final text width
            for (; charIndex < text.Length; charIndex++)
            {
                ushort glyphIndex = glyphTypeface_.CharacterToGlyphMap[text[charIndex]];
                tempGlyphIndexes[charIndex] = glyphIndex;

                double width = glyphTypeface_.AdvanceWidths[glyphIndex] * FontSize;
                tempAdvanceWidths[charIndex] = width;

                maxHeight = Math.Max(glyphTypeface_.AdvanceHeights[glyphIndex] * FontSize, maxHeight);

                totalTextWidth += width;

                if (totalTextWidth > desiredTextWidth)
                {
                    //we need to clip the text since it doesn't fit the allowed width
                    //do a second measurement pass
                    needDoTDotDot = true;
                    break;
                }
            }

            if (bEnableDotDotDot && needDoTDotDot)
            {
                ushort suffixGlyphIndex = glyphTypeface_.CharacterToGlyphMap['.'];
                double suffixWidth = glyphTypeface_.AdvanceWidths[suffixGlyphIndex] * FontSize;

                desiredTextWidth -= suffixWidth * 3;

                for (; charIndex > 0; charIndex--)
                {
                    double removedCharacterWidth = tempAdvanceWidths[charIndex];

                    totalTextWidth -= removedCharacterWidth;

                    if (totalTextWidth <= desiredTextWidth)
                    {
                        charIndex--;
                        break;
                    }
                }

                int finalNumCharacters = charIndex + 1 + 3;

                glyphIndexes = new ushort[finalNumCharacters];
                advanceWidths = new double[finalNumCharacters];

                Array.Copy(tempGlyphIndexes, glyphIndexes, charIndex + 1);
                Array.Copy(tempAdvanceWidths, advanceWidths, charIndex + 1);

                for (int i = charIndex + 1; i < finalNumCharacters; ++i)
                {
                    glyphIndexes[i] = suffixGlyphIndex;
                    advanceWidths[i] = suffixWidth;
                }
            }
            else
            {
                glyphIndexes = tempGlyphIndexes;
                advanceWidths = tempAdvanceWidths;
            }

            double roundedX = Math.Round(x);
            double roundedY = Math.Round(y + maxHeight);

            GlyphRun gr = new GlyphRun(
                glyphTypeface_,
                0,       // Bi-directional nesting level
                false,   // isSideways
                FontSize,      // pt size
                glyphIndexes,   // glyphIndices
                new Point(roundedX, roundedY),           // baselineOrigin
                advanceWidths,  // advanceWidths
                null,    // glyphOffsets
                null,    // characters
                null,    // deviceFontName
                null,    // clusterMap
                null,    // caretStops
                null);   // xmlLanguage

            dc.DrawGlyphRun(colorBrush, gr);
        }
    }
}
