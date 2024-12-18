using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
[RequireComponent(typeof(XRGrabInteractable))]
public class PayphoneHandset : MonoBehaviour
{

    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Transform originalParent;

    void Start()
    {
        preserveOriginalValues();
    }

    void preserveOriginalValues()
    {
        originalParent = gameObject.transform.parent;
        originalLocalPosition = gameObject.transform.localPosition;
        originalLocalRotation = gameObject.transform.localRotation;
    }

    void restoreOriginalValues()
    {
        gameObject.transform.parent = originalParent;
        gameObject.transform.localPosition = originalLocalPosition;
        gameObject.transform.localRotation = originalLocalRotation;
    }

    public void UnSelected()
    {
        restoreOriginalValues();
    }
}
