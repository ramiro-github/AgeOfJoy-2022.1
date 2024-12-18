using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class PayphoneCabin : MonoBehaviour
{

    public Transform playerTransform;
    public static string PreviousSceneName = "";
    private bool isMixedReality;
    private bool isLoadingScene = false;
    public GameObject effect;

    private void RepositionPayphoneCabin()
    {
        float yRotation = transform.rotation.y + playerTransform.rotation.y;
        transform.position = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
        transform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    void Start()
    {
        PreviousSceneName = SceneManager.GetActiveScene().name;
        isMixedReality = SceneManager.GetActiveScene().name == "MixedReality" ? true : false;

        if (isMixedReality)
        {
            RepositionPayphoneCabin();
        }
    }

    private IEnumerator LoadSceneWithDelay()
    {

        Camera mainCamera = Camera.main;
        mainCamera.cullingMask = (1 << LayerMask.NameToLayer("PayphoneCabin")) | (1 << LayerMask.NameToLayer("Hand"));
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = Color.black;

        effect.SetActive(true); 

        if (!isMixedReality)
        {
            foreach (GameObject obj in FindObjectsOfType<GameObject>())
            {
                if (obj.scene.name == "DontDestroyOnLoad")
                {
                    Debug.Log("Destruindo objeto persistente: " + obj.name);
                    Destroy(obj);
                }
            }
        }
        
        yield return new WaitForSeconds(6);

        string scene = isMixedReality ? "FixedScene" : "MixedReality";
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    public void LoadScene()
    {
        if (isLoadingScene) return;

        isLoadingScene = true;
        StartCoroutine(LoadSceneWithDelay());
    }

    void Update()
    {
        if (Keyboard.current.leftCtrlKey.isPressed)
        {
            RepositionPayphoneCabin();
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log("[DEBUG] Keyboard.current.eKey.isPressed");
            LoadScene();
        }
    }
}
