using System;
using HybridShooter.Round;
using UnityEngine;

public class RoundTester : MonoBehaviour
{
    [SerializeField] private RoundSequencer roundSequencer;
    
    private void Start()
    {
        roundSequencer.StartRound();
    }
}
