using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HaruKitEvi.Fixtures.Logic
{
    /// <summary>Play Mode 觸發器：Start 時執行指定時機，用來測執行觀察與 Hold。</summary>
    public sealed class EviGraphRunner : MonoBehaviour
    {
        [SerializeField] private EviGraphDefinition definition;
        [SerializeField] private EviTiming timing = EviTiming.OnStart;
        [SerializeField] private int seed = 7;

        private async void Start()
        {
            if (definition == null)
            {
                Debug.LogError("[HaruKitEvi] 請指定 EviGraphDefinition。", this);
                return;
            }

            var runtimeGraph = definition.CreateRuntimeGraph();
            var pack = new EviPack { Seed = seed, Name = name };
            var cancellation = this.GetCancellationTokenOnDestroy();
            try
            {
                await runtimeGraph.TriggerAction(timing, pack, cancellation, "EviGraphRunner " + timing);
                Debug.Log($"[HaruKitEvi] {timing} 完成，共 {pack.Log.Count} 則訊息。", this);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // 執行器被銷毀，這條執行鏈到此為止。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
