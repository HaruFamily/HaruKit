using System;
using System.Collections.Generic;
using System.Reflection;

namespace HaruFamily.DependencyCore.GraphKit
{
    /// <summary>可供族索引探索的明確配對宣告。</summary>
    public interface IFormulaFamilyDeclaration { }

    /// <summary>公式族的反向索引。配對只讀 Slot 宣告；首次查詢探索一次，求值不反射。</summary>
    public static class FormulaFamily
    {
        private static readonly Dictionary<(Type, Type), Type> tokens = new();
        private static readonly HashSet<(Type, Type)> ambiguous = new();
        private static bool discovered;

        public static void Register(FormulaSlotBase slot)
        {
            if (slot.TokenType == null || slot is UnionSlotBase) return;
            var key = (slot.TokenType, slot.PackType);
            if (tokens.TryGetValue(key, out var family) && family != slot.FamilyType) ambiguous.Add(key);
            else tokens[key] = slot.FamilyType;
        }

        public static Type OfToken(Type token, Type pack)
        {
            Discover();
            var key = (token, pack);
            if (ambiguous.Contains(key)) throw new InvalidOperationException($"Token {token.FullName} 被多個公式族宣告。");
            if (tokens.TryGetValue(key, out var family)) return family;
            throw new InvalidOperationException($"Token {token.FullName} 沒有對應的 FormulaSlot 宣告。");
        }

        public static bool DeclaresToken(Type token, Type pack)
        {
            Discover();
            return tokens.ContainsKey((token, pack));
        }

        public static FormulaSlotBase CreateOutput(Type token, Type pack)
            => (FormulaSlotBase)Activator.CreateInstance(OfToken(token, pack), true);

        private static void Discover()
        {
            if (discovered) return;
            discovered = true;
            try { DiscoverTypes(); }
            catch { discovered = false; throw; }
        }

        private static void DiscoverTypes()
        {
            var graphAssembly = typeof(FormulaSlotBase).Assembly;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (assembly != graphAssembly && !Array.Exists(assembly.GetReferencedAssemblies(), a => a.Name == graphAssembly.GetName().Name)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException error) { types = error.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.ContainsGenericParameters
                        || !typeof(FormulaSlotBase).IsAssignableFrom(type)
                        || !typeof(IFormulaFamilyDeclaration).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) == null) continue;
                    Register((FormulaSlotBase)Activator.CreateInstance(type, true));
                }
            }
        }
    }
}
