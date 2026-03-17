using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;
using System.Collections.Generic;
using Application = UnityEngine.Application;
using Google.MaterialDesign.Icons;
using UnityEngine.Android; // Required for Android-specific permission handling

public class LivekitSamples : MonoBehaviour
{

    [Header("LiveKit Connection")]
    public string url = "ws://localhost:7880";
    public string token = "YOUR_TOKEN";

    [Header("UI Buttons")]
    public Button CameraButton;
    public Button MicrophoneButton;
    public Button StartCallButton;
    public Button EndCallButton;
    public Button PublishDataButton;

    [Header("Other")]
    public GridLayoutGroup VideoTrackParent;

    private Room room = null;

    private WebCamTexture webCamTexture = null;

    private int frameRate = 30;

    Dictionary<string, GameObject> _videoGameObjects = new();
    Dictionary<string, ResizeController> _resizeControllers = new();
    Dictionary<string, GameObject> _audioGameObjects = new();
    RtcVideoSource _rtcVideoSource;
    RtcAudioSource _rtcAudioSource;
    List<VideoStream> _videoStreams = new();

    private Transform AudioTrackParent;


    private List<Button> InCallButtons;

    private const string LOCAL_VIDEO_TRACK_NAME = "my-video-track";
    private LocalVideoTrack _localVideoTrack;
    private bool _cameraActive = false;

    private const string LOCAL_AUDIO_TRACK_NAME = "my-audio-track";
    private LocalAudioTrack _localAudioTrack;
    private bool _microphoneActive = false;

    public void Start()
    {
        StartCallButton.onClick.AddListener(OnClickStartCall);
        CameraButton.onClick.AddListener(OnClickCamButton);
        MicrophoneButton.onClick.AddListener(OnClickMicrophoneButton);
        EndCallButton.onClick.AddListener(OnClickEndCall);
        PublishDataButton.onClick.AddListener(OnClickPublishData);

        InCallButtons = new List<Button>{CameraButton, MicrophoneButton, EndCallButton, PublishDataButton};

        AudioTrackParent = new GameObject("AudioTrackParent").transform;
    }

    public void Update()
    {
        foreach (var resizeCropController in _resizeControllers.Values)
        {
            resizeCropController.Resize();
        }
    }

    private void UpdateUi(bool connected)
    {
        foreach (var button in InCallButtons)
        {
            button.interactable = connected;
        }
        StartCallButton.interactable = !connected;

        // Reset button icons into default state
        if (connected == false)
        {
            MicrophoneButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e02b";
            CameraButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e04c";
        }
    }

    public void OnClickMicrophoneButton()
    {
        if (_microphoneActive == false)
        {
            StartCoroutine(PublishLocalMicrophone());
            MicrophoneButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e029";
        }
        else
        {
            UnpublishLocalMicrophone();
            MicrophoneButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e02b";
        }
        Debug.Log("OnClickPublishAudio clicked!");
    }

    public void OnClickCamButton()
    {
        if (_cameraActive == false)
        {
            StartCoroutine(PublishLocalCamera());
            CameraButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e04b";
        }
        else
        {
            UnpublishLocalCamera();
            CameraButton.GetComponentInChildren<MaterialIcon>().iconUnicode = "e04c";
        }

        Debug.Log("OnClickCamButton clicked!");
    }

    public void OnClickPublishData()
    {
        PublishData();
        Debug.Log("OnClickPublishData clicked!");
    }

    public void OnClickStartCall()
    {
        Debug.Log("OnClickStartCall clicked!");
        if (webCamTexture == null)
        {
            // Check if the platform is Android
#if PLATFORM_ANDROID
            // Check if camera permission is already granted
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                // Request camera permission from the user
                Permission.RequestUserPermission(Permission.Camera);
            }
#endif
            StartCoroutine(OpenCamera());
        }

        StartCoroutine(StartCall());
    }

    public void OnClickEndCall()
    {
        Debug.Log("OnClickEndCall clicked!");
        room.Disconnect();
        CleanUp();
        room = null;
        UpdateUi(false);
    }

    IEnumerator StartCall()
    {
        if (room == null)
        {
            room = new Room();
            room.TrackSubscribed += TrackSubscribed;
            room.TrackUnsubscribed += TrackUnsubscribed;
            room.DataReceived += DataReceived;
            var options = new RoomOptions();
            var connect = room.Connect(url, token, options);
            yield return connect;
            if (!connect.IsError)
            {
                Debug.Log("Connected to " + room.Name);
                UpdateUi(true);
            }
            else
            {
                Debug.Log("Connection failed");
            }
        }
    }

    void CleanUp()
    {
        _rtcAudioSource?.Stop();
        _rtcAudioSource?.Dispose();
        _rtcAudioSource = null;

        foreach (var item in _audioGameObjects)
        {
            var source = item.Value.GetComponent<AudioSource>();
            source.Stop();
            Destroy(item.Value);
        }

        _audioGameObjects.Clear();

        _rtcVideoSource?.Stop();
        _rtcVideoSource?.Dispose();
        _rtcVideoSource = null;;

        foreach (var item in _videoGameObjects)
        {
            RawImage img = item.Value.GetComponent<RawImage>();
            if (img != null)
            {
                img.texture = null;
                Destroy(img);
            }

            Destroy(item.Value);
        }

        _videoGameObjects.Clear();

        foreach (var resizeCropController in _resizeControllers.Values)
        {
            resizeCropController.Dispose();
        }

        _resizeControllers.Clear();

        foreach (var item in _videoStreams)
        {
            item.Stop();
            item.Dispose();
        }

        _videoStreams.Clear();

        _cameraActive = false;
        _microphoneActive = false;
    }

    void AddRemoteVideoTrack(RemoteVideoTrack videoTrack)
    {
        Debug.Log("AddVideoTrack " + videoTrack.Sid);

        GameObject imageObject = new GameObject(videoTrack.Sid);

        RectTransform trans = imageObject.AddComponent<RectTransform>();
        trans.localScale = Vector3.one;
        trans.sizeDelta = new Vector2(180, 120);
        trans.rotation = Quaternion.AngleAxis(Mathf.Lerp(0f, 180f, 50), Vector3.forward);

        RawImage image = imageObject.AddComponent<RawImage>();

        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += (tex) =>
        {
            var resizeController = new ResizeController(tex, VideoTrackParent.cellSize.x, VideoTrackParent.cellSize.y);
            image.texture = resizeController.GetTargetTexture();
            _resizeControllers.Add(videoTrack.Sid, resizeController);
        };

        _videoGameObjects[videoTrack.Sid] = imageObject;

        imageObject.transform.SetParent(VideoTrackParent.gameObject.transform, false);
        stream.Start();
        StartCoroutine(stream.Update());
        _videoStreams.Add(stream);
    }

    void AddRemoteAudioTrack(RemoteAudioTrack audioTrack)
    {
        Debug.Log("AddAudioTrack " + audioTrack.Sid);
        GameObject audioObject = new GameObject($"AudioTrack: {audioTrack.Sid}");
        audioObject.transform.SetParent(AudioTrackParent);
        var source = audioObject.AddComponent<AudioSource>();
        
        _ = new AudioStream(audioTrack, source);
        _audioGameObjects[audioTrack.Sid] = audioObject;
    }

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            AddRemoteVideoTrack(videoTrack);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            AddRemoteAudioTrack(audioTrack);
        }
    }

    void TrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            var imageObject = _videoGameObjects[videoTrack.Sid];
            if (imageObject != null)
            {
                Destroy(imageObject);
            }
            _videoGameObjects.Remove(videoTrack.Sid);

            var resizeCropController = _resizeControllers[videoTrack.Sid];
            resizeCropController.Dispose();
            _resizeControllers.Remove(videoTrack.Sid);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            var audioObject = _audioGameObjects[audioTrack.Sid];
            if (audioObject != null)
            {
                var source = audioObject.GetComponent<AudioSource>();
                source.Stop();
                Destroy(audioObject);
            }
            _audioGameObjects.Remove(audioTrack.Sid);
        }
    }

    void DataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var str = System.Text.Encoding.Default.GetString(data);
        Debug.Log("DataReceived: from " + participant.Identity + ", data " + str);
    }

    public IEnumerator PublishLocalMicrophone()
    {
        if (_audioGameObjects.ContainsKey(LOCAL_AUDIO_TRACK_NAME))
        {
            yield break;
        }

        GameObject audioObject = new GameObject($"My Microphone: {Microphone.devices[0]}");
        audioObject.transform.SetParent(AudioTrackParent);

        var rtcSource = new MicrophoneSource(Microphone.devices[0], audioObject);

        Debug.Log($"CreateAudioTrack");
        _localAudioTrack = LocalAudioTrack.CreateAudioTrack(LOCAL_AUDIO_TRACK_NAME, rtcSource, room);

        var options = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding
            {
                MaxBitrate = 64000
            },
            Source = TrackSource.SourceMicrophone
        };

        Debug.Log("PublishTrack!");
        var publish = room.LocalParticipant.PublishTrack(_localAudioTrack, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
            _microphoneActive = true;
            _audioGameObjects.Add(LOCAL_AUDIO_TRACK_NAME, audioObject);
            _rtcAudioSource = rtcSource;
            rtcSource.Start();
        }
    }

    public void UnpublishLocalMicrophone()
    {
        _rtcAudioSource?.Stop();
        _rtcAudioSource?.Dispose();
        _rtcAudioSource = null;

        if (_audioGameObjects.TryGetValue(LOCAL_AUDIO_TRACK_NAME, out var audioGameObject))
        {
            var source = audioGameObject.GetComponent<AudioSource>();
            source.Stop();
            Destroy(audioGameObject);

            _audioGameObjects.Remove(LOCAL_AUDIO_TRACK_NAME);
        }

        room.LocalParticipant.UnpublishTrack(_localAudioTrack, false);

        _microphoneActive = false;
    }

    public IEnumerator PublishLocalCamera()
    {
        if (_videoGameObjects.ContainsKey(LOCAL_VIDEO_TRACK_NAME))
        {
            yield break;
        }

        var source = new WebCameraSource(webCamTexture);

        GameObject imageObject = new GameObject("My Camera: " + webCamTexture.deviceName);
        RectTransform trans = imageObject.AddComponent<RectTransform>();
        trans.localScale = Vector3.one;
        trans.sizeDelta = new Vector2(180, 120);
        RawImage image = imageObject.AddComponent<RawImage>();
        source.TextureReceived += (tex) =>
        {
            var resizeController = new ResizeController(tex, VideoTrackParent.cellSize.x, VideoTrackParent.cellSize.y);
            image.texture = resizeController.GetTargetTexture();
            _resizeControllers.Add(LOCAL_VIDEO_TRACK_NAME, resizeController);
        };
        
        imageObject.transform.SetParent(VideoTrackParent.gameObject.transform, false);
        _localVideoTrack = LocalVideoTrack.CreateVideoTrack(LOCAL_VIDEO_TRACK_NAME, source, room);

        var videoCoding = new VideoEncoding
        {
            MaxBitrate = 512000,
            MaxFramerate = frameRate
        };
        var options = new TrackPublishOptions
        {
            VideoCodec = VideoCodec.H265,
            VideoEncoding = videoCoding,
            Simulcast = false,
            Source = TrackSource.SourceCamera
        };

        var publish = room.LocalParticipant.PublishTrack(_localVideoTrack, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
            _cameraActive = true;
            _videoGameObjects.Add(LOCAL_VIDEO_TRACK_NAME, imageObject);
            source.Start();
            StartCoroutine(source.Update());
            _rtcVideoSource = source;
        }
    }

    public void UnpublishLocalCamera()
    {
        _rtcVideoSource?.Stop();
        _rtcVideoSource?.Dispose();
        _rtcVideoSource = null;

        if (_videoGameObjects.TryGetValue(LOCAL_VIDEO_TRACK_NAME, out var videoGameObject))
        {
            RawImage img = videoGameObject.GetComponent<RawImage>();
            if (img != null)
            {
                img.texture = null;
                Destroy(img);
            }

            Destroy(videoGameObject);
            _videoGameObjects.Remove(LOCAL_VIDEO_TRACK_NAME);
        }

        if (_resizeControllers.TryGetValue(LOCAL_VIDEO_TRACK_NAME, out var resizeCropController))
        { 
            resizeCropController.Dispose();
            _resizeControllers.Remove(LOCAL_VIDEO_TRACK_NAME);
        }

        room.LocalParticipant.UnpublishTrack(_localVideoTrack, false);

        _cameraActive = false;
    }

    public void PublishData()
    {
        var str = "hello from unity!";
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str));
    }

    public IEnumerator OpenCamera()
    {
        int maxl = Screen.width;
        if (Screen.height > Screen.width)
        {
            maxl = Screen.height;
        }

        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
            }

            int i = 0;
            while (WebCamTexture.devices.Length <= 0 && 1 < 300)
            {
                yield return new WaitForEndOfFrame();
                i++;
            }
            WebCamDevice[] devices = WebCamTexture.devices;
            if (WebCamTexture.devices.Length <= 0)
            {
                Debug.LogError("No camera device available, please check");
            }
            else
            {
                string devicename = devices[0].name;
                webCamTexture = new WebCamTexture(devicename, maxl, maxl == Screen.height ? Screen.width : Screen.height, frameRate)
                {
                    wrapMode = TextureWrapMode.Repeat
                };

                webCamTexture.Play();
            }

        }
        else
        {
            Debug.LogError("Camera permission not obtained");
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (webCamTexture != null)
        {
            if (pause)
            {
                webCamTexture.Pause();
            }
            else
            {
                webCamTexture.Play();
            }
        }

    }


    private void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }
    }
}
