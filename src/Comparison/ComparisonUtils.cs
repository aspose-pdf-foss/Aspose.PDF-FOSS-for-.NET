using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Shared helpers for the document comparers.</summary>
    internal class ComparisonUtils
    {
        /// <summary>Extend <paramref name="excludeAreas"/> with the bounding rectangles of
        /// every table detected on <paramref name="page"/> (the ExcludeTables option).</summary>
        internal static Rectangle[] AddTablesToExcludeAreas(Rectangle[]? excludeAreas, Page? page)
        {
            var areas = new List<Rectangle>();
            if (excludeAreas is not null)
                foreach (var area in excludeAreas)
                    if (area is not null)
                        areas.Add(area);

            if (page is not null)
            {
                var absorber = new TableAbsorber();
                absorber.Visit(page);
                foreach (var table in absorber.TableList)
                    if (table.Rect is not null)
                        areas.Add(table.Rect);
            }
            return areas.ToArray();
        }
    }
}
