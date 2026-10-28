using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossController))]
    public class BossPatternRunner : MonoBehaviour
    {
        [SerializeField] private BossAttack[] attacks;
        private BossController boss;
        private readonly List<BossAttack> candidates = new();
        
        private void Awake()
        {
            boss = GetComponent<BossController>();
        }

        private void Update()
        {
            if (boss.CanStartAttack == false || attacks == null) return;
            
            candidates.Clear();

            foreach (var attack in attacks)
            {
                if (attack == null || attack.gameObject != gameObject || attack.CanStart() == false || candidates.Contains(attack)) continue;
                
                candidates.Add(attack);
            }

            if (candidates.Count == 0) return;

            int index = Random.Range(0, candidates.Count);
            BossAttack selected = candidates[index];

            if (selected.TryStart())
            {
                Debug.Log($"[BossPattern] 시작: {selected.GetType().Name}", this);
            }
        }
    }
}
