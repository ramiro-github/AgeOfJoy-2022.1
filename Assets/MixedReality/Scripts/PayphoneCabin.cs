using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class PayphoneCabin : MonoBehaviour
{

    public GameObject player;
    public static string PreviousSceneName = "";
    private bool isMixedReality;
    public bool isLoadingScene = false;
    public GameObject effect;
    public Transform referenceCabin;
    public AudioSource collision;
    public AudioSource travel;
    public AudioSource phoneCall;
    public AudioSource toInfinityAndBeyond;
    public PayphoneHandset payphoneHandset;
    private bool isTravel = false;
    private bool isTravelCancel = false;
    public GameObject smoke;

    private void RepositionPayphoneCabin()
    {
        float x = player.transform.position.x;
        float y = transform.position.y;
        float z = player.transform.position.z;

        transform.position = new Vector3(x, y, z);
    }

    private void RepositionPlayerPayphoneCabin()
    {
        float x = referenceCabin.position.x;
        float y = player.transform.position.y;
        float z = referenceCabin.position.z;

        player.transform.position = new Vector3(x, y, z);
    }

    IEnumerator WaitForPlayer()
    {

        while (player == null)
        {
            player = GameObject.Find("Complete XR Origin Set Up");
            yield return null;
        }

        RepositionPlayerPayphoneCabin();
    }

    void Start()
    {
        isMixedReality = SceneManager.GetActiveScene().name == "MixedReality" ? true : false;

        if (isMixedReality)
        {
            collision.Play();
            smoke.SetActive(true);
            RepositionPayphoneCabin();
        }
        else if (PreviousSceneName == "MixedReality")
        {
            collision.Play();
            smoke.SetActive(true);
            StartCoroutine(WaitForPlayer());
        }

        PreviousSceneName = SceneManager.GetActiveScene().name;
    }

    private IEnumerator LoadSceneAfterTravel()
    {

        float maxWaitTime = Mathf.Min(15f, travel.clip.length - travel.time);

        yield return new WaitUntil(() =>
       (!travel.isPlaying && travel.time >= travel.clip.length) || maxWaitTime <= 0);

        string scene = isMixedReality ? "FixedScene" : "MixedReality";
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    private IEnumerator PlayAudioEffect()
    {
        float maxWaitTime = Mathf.Min(7f, phoneCall.clip.length - phoneCall.time);
        phoneCall.Play();


        yield return new WaitUntil(() =>
      (!phoneCall.isPlaying && phoneCall.time >= phoneCall.clip.length) || maxWaitTime <= 0);

        maxWaitTime = Mathf.Min(4f, toInfinityAndBeyond.clip.length - toInfinityAndBeyond.time);
        toInfinityAndBeyond.Play();

        yield return new WaitUntil(() =>
           (!toInfinityAndBeyond.isPlaying && toInfinityAndBeyond.time >= toInfinityAndBeyond.clip.length) || maxWaitTime <= 0);

        if (payphoneHandset.selected)
        {
            payphoneHandset.ForceUnSelected();
        }

        BeforeTravel();

        effect.SetActive(true);

        travel.Play();

        isTravel = true;
        StartCoroutine(LoadSceneAfterTravel());
    }

    private void StopAllAudioSceneVR()
    {
        AudioSource[] audioSources = FindObjectsOfType<AudioSource>();

        foreach (AudioSource audio in audioSources)
        {
            if (audio.isPlaying)
            {
                audio.Stop();
            }
        }
    }

    private void BeforeTravel()
    {
        StopAllAudioSceneVR();

        Camera mainCamera = Camera.main;
        mainCamera.cullingMask = (1 << LayerMask.NameToLayer("PayphoneCabin")) | (1 << LayerMask.NameToLayer("Hand"));
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = Color.black;
    }

    public void LoadScene()
    {

        phoneCall.mute = false;
        toInfinityAndBeyond.mute = false;

        if (isLoadingScene) return;

        isLoadingScene = true;

        StartCoroutine(PlayAudioEffect());
    }

    public void StopLoadScene()
    {

        isLoadingScene = false;
        isTravel = false;

        phoneCall.Stop();
        toInfinityAndBeyond.Stop();
    }

    public void MuteEffectAudio()
    {
        phoneCall.mute = true;
        toInfinityAndBeyond.mute = true;
    }

    void Update()
    {
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            LoadScene();
        }
    }
}
