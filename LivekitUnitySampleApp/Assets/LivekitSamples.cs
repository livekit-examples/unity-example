using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.IO;
using Application = UnityEngine.Application;
using UnityEngine.Timeline;

public class LivekitSamples : MonoBehaviour
{
    public string url = "ws://192.168.1.141:7880";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3MTE2ODE2NDgsImlzcyI6IkFQSXJramtRYVZRSjVERSIsIm5hbWUiOiJ1bml0eSIsIm5iZiI6MTcwOTg4MTY0OCwic3ViIjoidW5pdHkiLCJ2aWRlbyI6eyJjYW5VcGRhdGVPd25NZXRhZGF0YSI6dHJ1ZSwicm9vbSI6ImxpdmUiLCJyb29tSm9pbiI6dHJ1ZX19.rBKKcMal3xADA0nVN9AnntQ3-c5-lqNxySYOJh97Ii4";

    private Room room = new Room();

    private WebCamTexture webCamTexture;

    private int frameRate = 30;

    Dictionary<string, GameObject> _remoteVideoObjects = new();
    Dictionary<string, GameObject> _remoteAudioObjects = new();

    public GridLayoutGroup layoutGroup; //Component

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
        StartCoroutine(OpenCamera());
        Debug.Log("OnClickPublishVideo clicked!");
    }

    public void onClickPublishData()
    {
        StartCoroutine(publishData());
        Debug.Log("onClickPublishData clicked!");
    }

    public void onClickHangup()
    {
        Debug.Log("onClickHangup clicked!");
    }

    void AddVideoTrack(RemoteVideoTrack videoTrack)
    {
        GameObject imgObject = new GameObject(videoTrack.Sid);

        RectTransform trans = imgObject.AddComponent<RectTransform>();
        trans.localScale = Vector3.one;
        trans.sizeDelta = new Vector2(180, 120);
        trans.rotation = Quaternion.AngleAxis(Mathf.Lerp(0f, 180f, 50), Vector3.forward);

        RawImage image = imgObject.AddComponent<RawImage>();

        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += (tex) =>
        {
            RawImage img = imgObject.GetComponent<RawImage>();
            if(img != null)
            {
                image.texture = tex;
            }
        };
     
        _remoteVideoObjects[videoTrack.Sid] = imgObject;

        imgObject.transform.SetParent(layoutGroup.gameObject.transform, false);

        StartCoroutine(stream.Update());
    }

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            AddVideoTrack(videoTrack);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            GameObject audObject = new GameObject(audioTrack.Sid);
            var source = audObject.AddComponent<AudioSource>();
            var stream = new AudioStream(audioTrack, source);
            // Audio is being played on the source ..
            source.Play();

            _remoteAudioObjects[audioTrack.Sid] = audObject;
        }
    }

    void UnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            var imgObject = _remoteVideoObjects[videoTrack.Sid];
            if(imgObject != null)
            {
                Destroy(imgObject);
            }
            _remoteVideoObjects.Remove(videoTrack.Sid);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            var audObject = _remoteAudioObjects[audioTrack.Sid];
            if (audObject != null)
            {
                var source = audObject.GetComponent<AudioSource>();
                source.Stop();
                Destroy(audObject);
            }
            _remoteAudioObjects.Remove(audioTrack.Sid);
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
        var localSid = "my-audio-source";
        GameObject audObject = new GameObject(localSid);
        var source = audObject.AddComponent<AudioSource>();
        source.clip = Microphone.Start(Microphone.devices[0], true, 2, 48000);
        source.loop = true;
        source.Play();

        _remoteAudioObjects[localSid] = audObject;

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

        var track = LocalVideoTrack.CreateVideoTrack("my-video-track", source);

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

                GameObject imgObject = new GameObject("camera");
                RectTransform trans = imgObject.AddComponent<RectTransform>();
                trans.localScale = Vector3.one;
                trans.sizeDelta = new Vector2(180, 120);
                RawImage image = imgObject.AddComponent<RawImage>();
                image.texture = webCamTexture;
                imgObject.transform.SetParent(layoutGroup.gameObject.transform, false);
                webCamTexture.Play();
            }

        }
        else
        {
            Debug.LogError("Camera permission not obtained");
        }

        publicVideo();
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
