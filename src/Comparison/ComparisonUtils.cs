using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Shared helpers for the document comparers.</summary>
    internal static class ComparisonUtils
    {
        /// <summary>Extend <paramref name="excludeAreas"/> with the bounding rectangles of
        /// every table detected on <paramref name="page"/> (the ExcludeTables option).</summary>
        internal static Rectangle[] AddTablesToExcludeAreas(Rectangle[]? excludeAreas, Page? page)
        {
            var areas = new List<Rectangle>();
            if (excludeAreas is not null)
                foreach (var area in excludeAreas.Where(area => area is not null))
                    areas.Add(area);

            if (page is not null)
            {
                var absorber = new TableAbsorber();
                absorber.Visit(page);
#pragma warning disable S3267 // the if's null test narrows the item for the body - a Where predicate does not carry that null state into the loop
                foreach (var table in absorber.TableList)
#pragma warning restore S3267
                    if (table.Rect is not null)
                        areas.Add(table.Rect);
            }
            return areas.ToArray();
        }
    }
}
