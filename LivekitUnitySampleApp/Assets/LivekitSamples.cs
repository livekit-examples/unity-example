using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Rooms;
using LiveKit.Rooms.Tracks;
using LiveKit.Rooms.TrackPublications;
using LiveKit.Rooms.Participants;
using LiveKit.Proto;
using UnityEngine.UI;
using System.Collections.Generic;
using Application = UnityEngine.Application;
using System.Threading;

public class LivekitSamples : MonoBehaviour
{
    public string url = "ws://192.168.1.141:7880";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3MTIwMTIxMzIsImlzcyI6IkFQSXJramtRYVZRSjVERSIsIm5hbWUiOiJ1bml0eSIsIm5iZiI6MTcxMDIxMjEzMiwic3ViIjoidW5pdHkiLCJ2aWRlbyI6eyJjYW5VcGRhdGVPd25NZXRhZGF0YSI6dHJ1ZSwicm9vbSI6ImxpdmUiLCJyb29tSm9pbiI6dHJ1ZX19.BGfv-u9u130rSRBhUwmimu8fQr4irmn1K1CH_3nkLdo";

    private Room room = null;

    private WebCamTexture webCamTexture = null;

    private int frameRate = 30;

    Dictionary<string, GameObject> _videoObjects = new();
    Dictionary<string, GameObject> _audioObjects = new();

    public GridLayoutGroup layoutGroup; //Component


    List<VideoStream> _videoStreams = new();
    List<RtcVideoSource> _rtcVideoSources = new();

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        foreach (var stream in _videoStreams)
        {
            stream.Update();
        }

        foreach (var source in _rtcVideoSources)
        {
            source.Update();
        }
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
        publishData();
        Debug.Log("onClickPublishData clicked!");
    }

    public void onClickMakeCall()
    {
        Debug.Log("onClickMakeCall clicked!");
        if(webCamTexture == null)
        {
            //StartCoroutine(OpenCamera());
        }
        
        StartCoroutine(MakeCall());
    }

    public void onClickHangup()
    {
        Debug.Log("onClickHangup clicked!");
        room?.Disconnect();
        CleanUp();
        room = null;
    }

    IEnumerator MakeCall()
    {
        if(room == null)
        {
            room = new Room();
            room.TrackSubscribed += TrackSubscribed;
            room.TrackUnsubscribed += UnTrackSubscribed;
            //room.DataReceived += DataReceived;
            var options = new LiveKit.Rooms.RoomOptions();

            var cancellationToken = new CancellationToken();
            var connect = room.Connect(url, token, options, cancellationToken);
            yield return connect;
            if (!connect.IsCompletedSuccessfully)
            {
                Debug.Log("Connected to " + room.Name);
            }
        }
        
    }

    void CleanUp()
    {
        foreach(var item in _audioObjects)
        {
            var source = item.Value.GetComponent<AudioSource>();
            source.Stop();
            Destroy(item.Value);
        }

        _audioObjects.Clear();

        foreach (var item in _videoObjects)
        {
            RawImage img = item.Value.GetComponent<RawImage>();
            if (img != null)
            {
                img.texture = null;
                Destroy(img);
            }

            Destroy(item.Value);
        }

        _videoObjects.Clear();
    }

    void AddVideoTrack(Track videoTrack)
    {
        GameObject imgObject = new GameObject(videoTrack.Sid);

        RectTransform trans = imgObject.AddComponent<RectTransform>();
        trans.localScale = Vector3.one;
        trans.sizeDelta = new Vector2(180, 120);
        trans.rotation = Quaternion.AngleAxis(Mathf.Lerp(0f, 180f, 50), Vector3.forward);

        RawImage image = imgObject.AddComponent<RawImage>();

        var stream = new VideoStream(videoTrack, VideoBufferType.Rgba);
        stream.TextureReceived += (tex) =>
        {
            image.texture = tex;
        };
     
        _videoObjects[videoTrack.Sid] = imgObject;

        imgObject.transform.SetParent(layoutGroup.gameObject.transform, false);

        stream.Start();

        _videoStreams.Add(stream);
    }

    void TrackSubscribed(ITrack track, TrackPublication publication, Participant participant)
    {
        if (track.Kind == TrackKind.KindVideo)
        {
            AddVideoTrack(track as Track);
        }
        else if (track.Kind == TrackKind.KindAudio)
        {
            GameObject audObject = new GameObject(track.Sid);
            var source = audObject.AddComponent<AudioSource>();
            var stream = new AudioStream(track, source);
            // Audio is being played on the source ..
            source.Play();
            _audioObjects[track.Sid] = audObject;
            stream.Start();
        }
    }

    void UnTrackSubscribed(ITrack track, TrackPublication publication, Participant participant)
    {
        if (track.Kind == TrackKind.KindVideo)
        {
            var imgObject = _videoObjects[track.Sid];
            if(imgObject != null)
            {
                Destroy(imgObject);
            }
            _videoObjects.Remove(track.Sid);
        }
        else if (track.Kind == TrackKind.KindAudio)
        {
            var audObject = _audioObjects[track.Sid];
            if (audObject != null)
            {
                var source = audObject.GetComponent<AudioSource>();
                source.Stop();
                Destroy(audObject);
            }
            _audioObjects.Remove(track.Sid);
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

        _audioObjects[localSid] = audObject;

        var audioFilter = new AudioFilter();
        var rtcSource = new RtcAudioSource(source, audioFilter);

        var track = room.TracksFactory.NewAudioTrack("my-audio-track", rtcSource, room);

        var options = new TrackPublishOptions();
        options.Source = TrackSource.SourceMicrophone;
        var token = new CancellationToken();
        var publish = room.Participants.LocalParticipant().PublishTrack(track, options, token);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
        }
    }

    public IEnumerator publicVideo()
    {
        //var source = new CameraVideoSource(Camera.allCameras[0], VideoBufferType.Argb);
        var source = new ScreenVideoSource();
        var track = room.TracksFactory.NewVideoTrack("my-video-track", source, room);

        var options = new TrackPublishOptions();
        options.VideoCodec = VideoCodec.Vp8;
        var videoCoding = new VideoEncoding();
        videoCoding.MaxBitrate = 512000;
        videoCoding.MaxFramerate = frameRate;
        options.VideoEncoding = videoCoding;
        options.Simulcast = true;
        options.Source = TrackSource.SourceCamera;
        var token = new CancellationToken();
        var publish = room.Participants.LocalParticipant().PublishTrack(track, options, token);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
        }

        source.Start();

        _rtcVideoSources.Add(source);
    }

    public void publishData()
    {
        var str = "hello from unity!";
        List<string> sids = new();
        room.DataPipe.PublishData(System.Text.Encoding.Default.GetBytes(str), null, sids);
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
