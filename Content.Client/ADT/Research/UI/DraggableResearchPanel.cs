using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using System.Linq;
using System.Numerics;

namespace Content.Client.ADT.Research.UI;

public sealed partial class DraggablePanel : LayoutContainer
{
    public DraggablePanel()
    {
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        foreach (var child in Children)
        {
            if (child is not ResearchConsoleItem item)
                continue;

            if (item.Prototype.RequiredTech.Count <= 0)
                continue;

            var list = Children.Where(x => x is ResearchConsoleItem second && item.Prototype.RequiredTech.Contains(second.Prototype.ID));

            foreach (var second in list)
            {
                // _Duty-start: центр карточки считаем от её реального размера, а не от хардкода 40:
                // при зуме размер меняется, и линии иначе отъезжают от карточек.
                var startCoords = new Vector2(item.PixelPosition.X + item.PixelWidth / 2f, item.PixelPosition.Y + item.PixelHeight / 2f);
                var endCoords = new Vector2(second.PixelPosition.X + second.PixelWidth / 2f, second.PixelPosition.Y + second.PixelHeight / 2f);
                // _Duty-end

                if (second.PixelPosition.Y != item.PixelPosition.Y)
                {

                    handle.DrawLine(startCoords, new(endCoords.X, startCoords.Y), Color.White);
                    handle.DrawLine(new(endCoords.X, startCoords.Y), endCoords, Color.White);
                }
                else
                {
                    handle.DrawLine(startCoords, endCoords, Color.White);
                }
            }
        }
    }
}
