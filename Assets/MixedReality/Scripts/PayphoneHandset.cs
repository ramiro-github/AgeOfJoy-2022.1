using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
[RequireComponent(typeof(XRGrabInteractable))]
public class PayphoneHandset : MonoBehaviour
{
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Transform originalParent;
    public PayphoneCabin payphoneCabin;
    public bool selected = false;
    private XRGrabInteractable grabInteractable;
    private bool canLoadingScene = true;

    void Start()
    {
        preserveOriginalValues();
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    void preserveOriginalValues()
    {
        originalParent = gameObject.transform.parent;
        originalLocalPosition = gameObject.transform.localPosition;
        originalLocalRotation = gameObject.transform.localRotation;
    }

    void RestoreOriginalValues()
    {
        gameObject.transform.parent = originalParent;
        gameObject.transform.localPosition = originalLocalPosition;
        gameObject.transform.localRotation = originalLocalRotation;
    }

    public void Selected()
    {
        selected = true;
    }

    public void UnSelected()
    {
        RestoreOriginalValues();
        payphoneCabin.StopLoadScene();
        selected = false;
    }

    public void ForceUnSelected()
    {
        IXRSelectInteractor currentInteractor = grabInteractable.interactorsSelecting[0];
        grabInteractable.interactionManager.SelectExit(currentInteractor, grabInteractable);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ear") && selected)
        {
            payphoneCabin.LoadScene();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("ear"))
        {
            payphoneCabin.MuteEffectAudio();
        }
    }

    void Update()
    {
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            UnSelected();
        }
    }
}
