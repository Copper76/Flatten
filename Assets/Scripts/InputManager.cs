using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager : MonoBehaviour
{

    [HideInInspector] public PlayerInputActions playerInputActions;

    private void Awake()
    {
        playerInputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        EnablePlayerInteraction();
    }

    private void OnDisable()
    {
        DisablePlayerInteraction();
    }

    public void DisablePlayerInteraction()
    {
        playerInputActions.Player.Look.Disable();
        playerInputActions.Player.Move.Disable();
        playerInputActions.Player.Jump.Disable();
        playerInputActions.Player.Fire.Disable();
    }
    public void EnablePlayerInteraction()
    {
        playerInputActions.Player.Look.Enable();
        playerInputActions.Player.Move.Enable();
        playerInputActions.Player.Jump.Enable();
        playerInputActions.Player.Fire.Enable();
    }
}
