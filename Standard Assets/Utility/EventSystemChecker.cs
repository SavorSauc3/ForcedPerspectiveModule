using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class EventSystemChecker : MonoBehaviour
{
    //public GameObject eventSystem;

    // Use this for initialization
    void Awake()
    {
        if (!FindObjectOfType<EventSystem>())
        {
            // Instantiate the EventSystem
            GameObject obj = new GameObject("EventSystem");
            obj.AddComponent<EventSystem>();
            obj.AddComponent<StandaloneInputModule>();
            // Removed the TouchInputModule component
        }
    }
}
