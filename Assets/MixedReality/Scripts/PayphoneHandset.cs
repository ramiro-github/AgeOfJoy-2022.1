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
    private bool selected = false;
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

    void restoreOriginalValues()
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
        selected = false;
        restoreOriginalValues();
    }

    private IEnumerator UnSelectedDelay()
    {
        yield return new WaitForSeconds(0.5f);

        IXRSelectInteractor currentInteractor = grabInteractable.interactorsSelecting[0];
        grabInteractable.interactionManager.SelectExit(currentInteractor, grabInteractable);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ear") && canLoadingScene && selected)
        {
            StartCoroutine(UnSelectedDelay());
            canLoadingScene = false;
            payphoneCabin.LoadScene();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("ear"))
        {
            canLoadingScene = true;
        }
    }
}
