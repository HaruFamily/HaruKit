using System;
using System.Collections.Generic;
using System.IO;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Tools.AssetPipeline;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaruKitEvi.PipelineSteps
{
    // 「依音效批次產生 Prefab」管線用的節點：收集音效 → 逐一處理（複製 Prefab、替換音效、登記 Addressable）。
    // 動作之間只透過 Property 傳資料；資產寫入一律經由 context.Assets，失敗時整次執行會回復。

    // ================= 公式 =================

    [Serializable]
    [HGNode("物件名稱", "取得資產或物件的名稱；沒有物件時回空字串", "Evi")]
    public sealed class PipeObjectName : Formula_String<NullPack>
    {
        [HGLabel("物件")] public ObjectSlot target = new ObjectSlot();

        protected override string OnEvaluate(NullPack pack)
        {
            Object value = target.Evaluate();
            return value != null ? value.name : string.Empty;
        }
    }

    // ================= 動作 =================

    [Serializable]
    [HGNode("收集音效", "找出資料夾（含子資料夾）裡的所有 AudioClip，寫進 Property；名稱重複時失敗", "Evi")]
    public sealed class PipeCollectAudioClips : ActionBase
    {
        [HGLabel("資料夾")] public FolderSlot folder = new FolderSlot();

        [HGLabel("輸出")]
        public PropertySlot<List<AudioClip>, AudioClipListSlot> output = new PropertySlot<List<AudioClip>, AudioClipListSlot>();

        protected override void OnExecute(PipelineActionContext context)
        {
            string path = folder.EvaluatePath();
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
            {
                context.Fail("音效資料夾無效。", path);
                return;
            }

            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { path }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            // 固定順序：每次執行的處理順序與結果列表一致。
            paths.Sort(StringComparer.Ordinal);

            var clips = new List<AudioClip>();
            // 後面用音效名稱當 Prefab 檔名與 Addressable 位址，名稱重複會互相覆寫，先在這裡擋下。
            var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string clipPath in paths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null) continue;
                if (pathByName.TryGetValue(clip.name, out string other))
                {
                    context.Fail($"音效名稱重複：'{clip.name}'（{other}）。", clipPath);
                    return;
                }
                pathByName.Add(clip.name, clipPath);
                clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                context.Skip("資料夾裡沒有音效。");
                return;
            }
            if (!output.Write(clips))
            {
                context.Fail("沒有指定要寫入的 Property。");
                return;
            }
            foreach (AudioClip clip in clips) context.Result.Record(PipelineItemStatus.Collected, clip);
        }
    }

    [Serializable]
    [HGNode("逐一處理音效", "對清單裡的每個音效：先寫進「目前音效」Property，再依序執行下面的動作", "Evi")]
    public sealed class PipeForEachAudioClip : ActionBase, ISequentialActionContainer, IGraphFlowContainer
    {
        [HGLabel("音效清單")] public AudioClipListSlot clips = new AudioClipListSlot();

        [HGLabel("目前音效")]
        public PropertySlot<AudioClip, AudioClipSlot> current = new PropertySlot<AudioClip, AudioClipSlot>();

        [HGLabel("每個音效執行")] public List<ActionSlot> steps = new List<ActionSlot>();

        IReadOnlyList<ActionSlotBase> ISequentialActionContainer.SequentialActions => steps;

        public GraphFlowDescription DescribeFlow() => new GraphFlowDescription(GraphFlowKind.Loop,
            new[] { new GraphFlowSection("body", "每個音效執行", GraphFlowSectionRole.Body, steps) },
            new[] { new GraphFlowInput("clips", "音效清單", clips) });

        protected override void OnExecute(PipelineActionContext context)
        {
            List<AudioClip> items = clips.Evaluate();
            if (items == null || items.Count == 0)
            {
                context.Skip("沒有音效要處理。");
                return;
            }
            if (current.Target == null)
            {
                context.Fail("沒有指定「目前音效」要寫入的 Property。");
                return;
            }

            // 先抄一份：子動作不該改到正在走訪的清單。
            var snapshot = new List<AudioClip>(items);
            int processed = 0;
            foreach (AudioClip clip in snapshot)
            {
                if (clip == null) continue;
                current.Write(clip);
                foreach (ActionSlot step in steps)
                {
                    if (step == null) continue;
                    step.Execute(context);
                    // 子動作共用這一步的結果；任何一個失敗就停，交給管線回復整次執行。
                    if (context.Result.HasFailure) return;
                }
                processed++;
            }
            context.Result.Message($"處理了 {processed} 個音效。");
        }
    }

    [Serializable]
    [HGNode("複製 Prefab", "把來源 Prefab 複製到輸出資料夾並指定檔名；同名已存在時覆寫內容並保留 GUID", "Evi")]
    public sealed class PipeCopyPrefabAs : ActionBase
    {
        [HGLabel("來源 Prefab")] public GameObjectSlot source = new GameObjectSlot();
        [HGLabel("輸出資料夾")] public FolderSlot outputFolder = new FolderSlot();
        [HGLabel("檔名")] public StringSlot fileName = new StringSlot(string.Empty);

        [HGLabel("輸出")]
        public PropertySlot<GameObject, GameObjectSlot> output = new PropertySlot<GameObject, GameObjectSlot>();

        protected override void OnExecute(PipelineActionContext context)
        {
            GameObject prefab = source.Evaluate();
            string sourcePath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                context.Fail("來源必須是 Prefab 資產。", sourcePath);
                return;
            }

            string folder = outputFolder.EvaluatePath();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                context.Fail("輸出資料夾無效。", folder);
                return;
            }

            string name = (fileName.Evaluate() ?? string.Empty).Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                context.Fail($"檔名無效：'{name}'。");
                return;
            }

            // Prefab 資產的根物件名稱跟著檔名走，所以「改名」就是用新檔名複製，不另外寫一次。
            GameObject copy = context.Assets.CopyPrefab(sourcePath, folder + "/" + name + ".prefab");
            if (!output.Write(copy))
                context.Result.Message("沒有指定輸出 Property，後續步驟讀不到這個 Prefab。");
        }
    }

    [Serializable]
    [HGNode("替換音效", "把 Prefab 裡第一個 AudioSource（含子物件）的 clip 換成指定音效；找不到 AudioSource 時失敗", "Evi")]
    public sealed class PipeSetAudioSourceClip : ActionBase
    {
        [HGLabel("Prefab")] public GameObjectSlot prefab = new GameObjectSlot();
        [HGLabel("音效")] public AudioClipSlot clip = new AudioClipSlot();

        protected override void OnExecute(PipelineActionContext context)
        {
            GameObject target = prefab.Evaluate();
            string path = target != null ? AssetDatabase.GetAssetPath(target) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                context.Fail("目標必須是 Prefab 資產。", path);
                return;
            }

            AudioClip value = clip.Evaluate();
            if (value == null)
            {
                context.Fail("沒有指定音效。", path);
                return;
            }
            if (target.GetComponentInChildren<AudioSource>(true) == null)
            {
                context.Fail("Prefab 裡找不到 AudioSource。", path);
                return;
            }

            context.Assets.EditPrefab(path, root =>
            {
                AudioSource audio = root.GetComponentInChildren<AudioSource>(true);
                if (audio == null) throw new InvalidOperationException("Prefab 裡找不到 AudioSource：" + path);
                audio.clip = value;
            });
        }
    }

    [Serializable]
    [HGNode("登記 Addressable", "把資產加入指定的 Addressable 群組並設定位址；已在其他群組時會移過來", "Evi")]
    public sealed class PipeRegisterAddressable : ActionBase
    {
        [HGLabel("資產")] public ObjectSlot asset = new ObjectSlot();

        [HGLabel("群組"), HGDescription("AddressableAssetGroup 資產")]
        public ObjectSlot group = new ObjectSlot();

        [HGLabel("位址")] public StringSlot address = new StringSlot(string.Empty);

        protected override void OnExecute(PipelineActionContext context)
        {
            Object target = asset.Evaluate();
            string path = target != null ? AssetDatabase.GetAssetPath(target) : null;
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                context.Fail("要登記的對象必須是專案裡的資產。", path);
                return;
            }

            var targetGroup = group.Evaluate() as AddressableAssetGroup;
            AddressableAssetSettings settings = targetGroup != null ? targetGroup.Settings : null;
            if (settings == null)
            {
                context.Fail("請指定 Addressable 群組資產（AddressableAssetGroup）。", path);
                return;
            }

            string name = (address.Evaluate() ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                context.Fail("Addressable 位址是空的。", path);
                return;
            }

            AddressableAssetEntry existing = settings.FindAssetEntry(guid);
            if (existing != null && existing.parentGroup == targetGroup && existing.address == name)
            {
                context.Result.Message($"已登記：{name}");
                return;
            }

            // 項目資料存在群組資產裡；從別的群組移過來時，兩個群組都要納入交易。
            var storage = new List<Object> { targetGroup };
            if (existing != null && existing.parentGroup != null && existing.parentGroup != targetGroup)
                storage.Add(existing.parentGroup);

            context.Assets.EditAssetSet(storage, target, () =>
            {
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, targetGroup, false, true);
                if (entry == null) throw new InvalidOperationException("無法建立 Addressable 項目：" + path);
                entry.SetAddress(name, true);
            }, $"{targetGroup.Name} ← {name}");
        }
    }
}
