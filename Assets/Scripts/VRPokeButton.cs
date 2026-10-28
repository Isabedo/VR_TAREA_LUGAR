using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

public class VRPokeButton : MonoBehaviour
{
    public enum ButtonActionType
    {
        ToggleRadio = 0,
        ToggleLights = 1,
        ToggleFireplace = 2
    }

    [Header("Configuración del Botón Poke")]
    public ButtonActionType actionType = ButtonActionType.ToggleRadio;
    public PokeInteractable pokeInteractable;
    public Transform buttonCapTransform;
    public Renderer buttonCapRenderer;
    public bool isOn = false;

    [Header("1. Radio (Arrastra tu AudioClip al AudioSource de radio_1 o radio_2)")]
    public AudioSource targetAudioSource;
    public AudioSource fallbackRadioAudioSource;

    [Header("2. Luces / Lámparas")]
    public Light[] targetLights;
    public Light directionalLight;
    public float directionalIntensityOn = 2.0f;
    public float directionalIntensityOff = 0.12f;

    [Header("3. Objetos Visuales a Encender/Apagar (ej. fireglow_mesh, bombillos)")]
    public GameObject[] targetObjectsToToggle;

    [Header("Ajustes Físicos del Poke")]
    public float pokeTriggerDistance = 0.055f;
    public float pokeReleaseDistance = 0.075f;
    public float cooldownSeconds = 0.45f;
    public Vector3 localPressDirection = new Vector3(0f, 0f, 1f);
    public float pressDepth = 0.22f;

    private Vector3 _initialCapLocalPos;
    private float _currentPressAmount = 0f;
    private float _lastToggleTime = -10f;
    private bool _proximityPressed = false;
    private PokeInteractor[] _cachedPokeInteractors;
    private float _nextInteractorSearchTime = 0f;
    private MaterialPropertyBlock _mpb;
    private AudioSource _clickAudioSource;
    private AudioClip _clickClip;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if ( pokeInteractable == null )
            pokeInteractable = GetComponent<PokeInteractable>();

        if ( buttonCapTransform != null )
            _initialCapLocalPos = buttonCapTransform.localPosition;

        _mpb = new MaterialPropertyBlock();
        CreateClickFeedbackAudio();
    }

    private void Start()
    {
        AutoBindFallbackReferences();
        ApplyInitialVisualState();
    }

    private void OnEnable()
    {
        if ( pokeInteractable != null )
        {
            pokeInteractable.WhenStateChanged += HandlePokeStateChanged;
        }
    }

    private void OnDisable()
    {
        if ( pokeInteractable != null )
        {
            pokeInteractable.WhenStateChanged -= HandlePokeStateChanged;
        }
    }

    private void HandlePokeStateChanged(InteractableStateChangeArgs args)
    {
        if ( args.NewState == InteractableState.Select )
        {
            TryToggle();
        }
    }

    private void Update()
    {
        if ( Time.time >= _nextInteractorSearchTime )
        {
            _cachedPokeInteractors = FindObjectsByType<PokeInteractor>(FindObjectsSortMode.None);
            _nextInteractorSearchTime = Time.time + 2.0f;
        }

        Transform checkPoint = buttonCapTransform != null ? buttonCapTransform : transform;
        Vector3 buttonWorldPos = checkPoint.position;

        float minDistance = float.MaxValue;
        if ( _cachedPokeInteractors != null )
        {
            for ( int i = 0; i < _cachedPokeInteractors.Length; i++ )
            {
                PokeInteractor interactor = _cachedPokeInteractors[i];
                if ( interactor == null || !interactor.isActiveAndEnabled )
                    continue;

                float dist = Vector3.Distance(interactor.Origin, buttonWorldPos);
                if ( dist < minDistance )
                    minDistance = dist;
            }
        }

        if ( !_proximityPressed && minDistance <= pokeTriggerDistance )
        {
            _proximityPressed = true;
            TryToggle();
        }
        else if ( _proximityPressed && minDistance >= pokeReleaseDistance )
        {
            _proximityPressed = false;
        }

        bool isSelectedOrPressed = _proximityPressed ||
                                   (pokeInteractable != null && pokeInteractable.State == InteractableState.Select) ||
                                   (Time.time - _lastToggleTime < 0.18f);

        float targetPress = isSelectedOrPressed ? 1f : 0f;
        _currentPressAmount = Mathf.MoveTowards(_currentPressAmount, targetPress, Time.deltaTime * 10f);

        if ( buttonCapTransform != null )
        {
            buttonCapTransform.localPosition = _initialCapLocalPos + localPressDirection.normalized * (pressDepth * _currentPressAmount);
        }
    }

    private void OnMouseDown()
    {
        TryToggle();
    }

    public void TryToggle()
    {
        if ( Time.time - _lastToggleTime < cooldownSeconds )
            return;

        _lastToggleTime = Time.time;
        isOn = !isOn;

        PlayClickSound();
        ExecuteAction(isOn);
        UpdateButtonColor();

        // Sincronizar otros botones del mismo tipo (por ejemplo, si hay botón en ambas lámparas)
        if ( actionType == ButtonActionType.ToggleLights || actionType == ButtonActionType.ToggleFireplace )
        {
            VRPokeButton[] allButtons = FindObjectsByType<VRPokeButton>(FindObjectsSortMode.None);
            for ( int i = 0; i < allButtons.Length; i++ )
            {
                if ( allButtons[i] != this && allButtons[i].actionType == actionType )
                {
                    allButtons[i].isOn = isOn;
                    allButtons[i].UpdateButtonColor();
                }
            }
        }
    }

    private void ExecuteAction(bool state)
    {
        switch ( actionType )
        {
            case ButtonActionType.ToggleRadio:
                ToggleRadioAudio(state);
                break;

            case ButtonActionType.ToggleLights:
                ToggleSceneLights(state);
                break;

            case ButtonActionType.ToggleFireplace:
                ToggleFireplaceState(state);
                break;
        }
    }

    private void ToggleRadioAudio(bool play)
    {
        if ( targetAudioSource == null )
            return;

        // Si el usuario puso su AudioClip en la otra radio, lo copiamos automáticamente
        if ( targetAudioSource.clip == null && fallbackRadioAudioSource != null && fallbackRadioAudioSource.clip != null )
        {
            targetAudioSource.clip = fallbackRadioAudioSource.clip;
        }

        if ( play )
        {
            if ( targetAudioSource.clip != null )
            {
                targetAudioSource.Play();
            }
            else
            {
                Debug.Log("[VRPokeButton] Radio encendida. Arrastra tu canción al campo 'AudioClip' del componente AudioSource en radio_1 o radio_2.");
            }
        }
        else
        {
            targetAudioSource.Stop();
        }
    }

    private void ToggleSceneLights(bool lightsOn)
    {
        if ( targetLights != null )
        {
            for ( int i = 0; i < targetLights.Length; i++ )
            {
                if ( targetLights[i] != null )
                    targetLights[i].enabled = lightsOn;
            }
        }

        if ( targetObjectsToToggle != null )
        {
            for ( int i = 0; i < targetObjectsToToggle.Length; i++ )
            {
                if ( targetObjectsToToggle[i] != null )
                    targetObjectsToToggle[i].SetActive(lightsOn);
            }
        }

        if ( directionalLight != null )
        {
            directionalLight.intensity = lightsOn ? directionalIntensityOn : directionalIntensityOff;
        }
    }

    private void ToggleFireplaceState(bool fireOn)
    {
        if ( targetLights != null )
        {
            for ( int i = 0; i < targetLights.Length; i++ )
            {
                if ( targetLights[i] != null )
                    targetLights[i].enabled = fireOn;
            }
        }

        if ( targetObjectsToToggle != null )
        {
            for ( int i = 0; i < targetObjectsToToggle.Length; i++ )
            {
                if ( targetObjectsToToggle[i] != null )
                    targetObjectsToToggle[i].SetActive(fireOn);
            }
        }
    }

    private void ApplyInitialVisualState()
    {
        if ( actionType == ButtonActionType.ToggleRadio && targetAudioSource != null )
        {
            isOn = targetAudioSource.isPlaying;
        }
        UpdateButtonColor();
    }

    public void UpdateButtonColor()
    {
        if ( buttonCapRenderer == null )
            return;

        if ( _mpb == null )
            _mpb = new MaterialPropertyBlock();

        Color activeColor;
        Color inactiveColor;

        switch ( actionType )
        {
            case ButtonActionType.ToggleRadio:
                activeColor = new Color(0.15f, 0.90f, 0.35f, 1f);   // Verde brillante cuando suena
                inactiveColor = new Color(0.85f, 0.20f, 0.20f, 1f); // Rojo suave cuando está apagada
                break;
            case ButtonActionType.ToggleLights:
                activeColor = new Color(1.00f, 0.85f, 0.25f, 1f);   // Amarillo cálido encendido
                inactiveColor = new Color(0.25f, 0.30f, 0.45f, 1f); // Azul nocturno apagado
                break;
            default:
                activeColor = new Color(1.00f, 0.42f, 0.10f, 1f);   // Naranja fuego encendido
                inactiveColor = new Color(0.35f, 0.25f, 0.22f, 1f); // Carbón apagado
                break;
        }

        Color chosen = isOn ? activeColor : inactiveColor;
        buttonCapRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, chosen);
        _mpb.SetColor(ColorId, chosen);
        buttonCapRenderer.SetPropertyBlock(_mpb);
    }

    private void AutoBindFallbackReferences()
    {
        if ( buttonCapRenderer == null && buttonCapTransform != null )
        {
            buttonCapRenderer = buttonCapTransform.GetComponentInChildren<Renderer>();
        }
    }

    private void CreateClickFeedbackAudio()
    {
        _clickAudioSource = gameObject.AddComponent<AudioSource>();
        _clickAudioSource.playOnAwake = false;
        _clickAudioSource.loop = false;
        _clickAudioSource.spatialBlend = 1.0f;
        _clickAudioSource.volume = 0.35f;

        int sampleRate = 22050;
        int sampleCount = sampleRate / 25; // ~40ms click
        float[] samples = new float[sampleCount];
        for ( int i = 0; i < sampleCount; i++ )
        {
            float t = (float)i / sampleCount;
            float env = (1f - t) * (1f - t);
            samples[i] = Mathf.Sin(2f * Mathf.PI * 650f * t) * env * 0.6f;
        }

        _clickClip = AudioClip.Create("PokeClickFeedback", sampleCount, 1, sampleRate, false);
        _clickClip.SetData(samples, 0);
        _clickAudioSource.clip = _clickClip;
    }

    private void PlayClickSound()
    {
        if ( _clickAudioSource != null && _clickClip != null )
        {
            _clickAudioSource.PlayOneShot(_clickClip);
        }
    }
}
