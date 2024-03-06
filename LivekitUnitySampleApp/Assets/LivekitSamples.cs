using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;

public class LivekitSamples : MonoBehaviour
{
    public string url = "ws://192.168.1.141:7880";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3MTAxOTE5ODQsImlzcyI6IkFQSXJramtRYVZRSjVERSIsIm5hbWUiOiJ1bml0eSIsIm5iZiI6MTcwODM5MTk4NCwic3ViIjoidW5pdHkiLCJ2aWRlbyI6eyJjYW5VcGRhdGVPd25NZXRhZGF0YSI6dHJ1ZSwicm9vbSI6ImxpdmUiLCJyb29tSm9pbiI6dHJ1ZX19.dq1-Rn29qR95iHhurUqwBAxORVpC7q2gz7-jX4rBkAs";

    private Room room = new Room();

    public RawImage remoteVideo;

    public RawImage localVideo;

    public AudioSource audioSource;

    private WebCamTexture webCamTexture;

    private int frameRate = 30;

    // Start is called before the first frame update
    IEnumerator Start()
    {
        room.TrackSubscribed += TrackSubscribed;
        room.TrackUnsubscribed += UnTrackSubscribed;
        room.DataReceived += DataReceived;
        var options = new RoomOptions();
        var connect = room.Connect(url, token, options);
        yield return connect;
        if (!connect.IsError)
        {
            Debug.Log("Connected to " + room.Name);
        }

        StartCoroutine(OpenCamera());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnClickPublishAudio()
    {
        StartCoroutine(publicMicrophone());
        Debug.Log("OnClickPublishAudio clicked!");
    }

    public void OnClickPublishVideo()
    {
        StartCoroutine(publicVideo());
        Debug.Log("OnClickPublishVideo clicked!");
    }

    public void onClickPublishData()
    {
        StartCoroutine(publishData());
        Debug.Log("onClickPublishData clicked!");
    }

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            //var rawImage = GetComponent<RawImage>();
            var stream = new VideoStream(videoTrack);
            stream.TextureReceived += (tex) =>
            {
                remoteVideo.texture = tex;
            };
            StartCoroutine(stream.Update());
            // The video data is displayed on the rawImage
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            //var source = GetComponent<AudioSource>();
            //var stream = new AudioStream(audioTrack, audioSource);
            // Audio is being played on the source ..
        }
    }

    void UnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            remoteVideo.texture = null;
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            audioSource.Stop();
        }
    }

    void DataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var str = System.Text.Encoding.Default.GetString(data);
        Debug.Log("DataReceived: from " + participant.Identity + ", data " + str);
    }

    public IEnumerator publicMicrophone()
    {
        // Publish Microphone
        var source = audioSource;
        source.clip = Microphone.Start(Microphone.devices[0], true, 2, 48000);
        source.loop = true;
        source.Play();

        var rtcSource = new RtcAudioSource(source);
        var track = LocalAudioTrack.CreateAudioTrack("my-audio-track", rtcSource);

        var options = new TrackPublishOptions();
        options.Source = TrackSource.SourceMicrophone;

        var publish = room.LocalParticipant.PublishTrack(track, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
        }
    }

    public IEnumerator publicVideo()
    {
        //var rt = new UnityEngine.RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        //rt.Create();
        //var source = new TextureVideoSource(remoteVideo.texture);

        var source = new TextureVideoSource(webCamTexture);

        var track = LocalVideoTrack.CreateVideoTrack("my-track", source);

        var options = new TrackPublishOptions();
        options.VideoCodec = VideoCodec.Vp8;
        var videoCoding = new VideoEncoding();
        videoCoding.MaxBitrate = 512000;
        videoCoding.MaxFramerate = frameRate;
        options.VideoEncoding = videoCoding;
        options.Simulcast = true;
        options.Source = TrackSource.SourceCamera;

        var publish = room.LocalParticipant.PublishTrack(track, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
        }

        StartCoroutine(source.Update());
    }

    public IEnumerator publishData()
    {
        var str = "hello from unity!";
        var publish = room.LocalParticipant.publishData(System.Text.Encoding.Default.GetBytes(str));
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Data published!");
        }
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

            if (localVideo != null)
            {
                localVideo.gameObject.SetActive(true);
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

                if (localVideo != null)
                {
                    localVideo.texture = webCamTexture;
                }

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
