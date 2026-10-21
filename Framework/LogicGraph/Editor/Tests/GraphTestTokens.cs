using System;
using HaruFamily.DependencyCore.GraphKit;

namespace HaruFamily.Framework.LogicGraph.Editor.Tests
{
    internal static class GraphTestTokens
    {
        internal static GraphToken Create(string name, FormulaSlotBase input)
        {
            var token = (GraphToken)Activator.CreateInstance(input.TokenType, true);
            if (token.Slot is UnionSlotBase union)
            {
                union.DefaultObject = input.DefaultObject;
                union.SetSelectedSlot(input);
            }
            else token.SetSlot(input);
            token.Name = name;
            return token;
        }
    }
}
