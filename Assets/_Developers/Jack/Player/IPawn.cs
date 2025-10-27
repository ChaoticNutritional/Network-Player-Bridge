using System;
using UnityEngine;
using PurrNet;

public interface IPawn
{
    // readonly network identity
    NetworkIdentity Identity { get; }

    
    void OnPossessed(PlayerAgent agent);
    void OnUnpossessed();
}
