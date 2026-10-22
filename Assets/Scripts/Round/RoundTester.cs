using System;
using HybridShooter.Round;
using UnityEngine;

public class RoundTester : MonoBehaviour
{
    [SerializeField] private RoundManager roundManager;
    
    private void Start()
    {
        roundManager.StartRound();
    }
}
