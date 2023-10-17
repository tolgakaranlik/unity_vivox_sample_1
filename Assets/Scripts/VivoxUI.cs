using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VivoxUnity;

public class VivoxUI : MonoBehaviour
{
    private const string LobbyChannelName = "lobbyChannel";

    public GameObject PnlLogin;
    public GameObject PnlLobby;
    public GameObject PlayerDisplayPrefab;
    public GameObject ChatContentObj;
    public GameObject MessageObject;
    public Transform PlayerDisplay;

    public Toggle ReadIncoming;
    public Button LoginButton;
    public Button SendButton;
    public Button SendTTSButton;
    public TMP_InputField InputUserName;
    public TMP_InputField InputMessage;
    public GameObject ConnectionIndicatorDot;
    public GameObject ConnectionIndicatorText;

    private Image _connectionIndicatorDotImage;
    private Text _connectionIndicatorDotText;
    private ChannelId _lobbyChannelId;
    private Dictionary<ChannelId, List<RosterItem>> rosterObjects = new Dictionary<ChannelId, List<RosterItem>>();
    private List<GameObject> _messageObjPool = new List<GameObject>();

    private void Awake()
    {
        VivoxVoiceManager.Instance.OnUserLoggedInEvent += OnUserLoggedIn;
        VivoxVoiceManager.Instance.OnUserLoggedOutEvent += OnUserLoggedOut;
        VivoxVoiceManager.Instance.OnTextMessageLogReceivedEvent += OnTextMessageLogReceivedEvent;

        VivoxVoiceManager.Instance.OnParticipantAddedEvent += OnParticipantAdded;
        VivoxVoiceManager.Instance.OnParticipantRemovedEvent += OnParticipantRemoved;
        VivoxVoiceManager.Instance.OnRecoveryStateChangedEvent += OnRecoveryStateChanged;

        _connectionIndicatorDotImage = ConnectionIndicatorDot.GetComponent<Image>();
        _connectionIndicatorDotText = ConnectionIndicatorText.GetComponent<Text>();

        if (VivoxVoiceManager.Instance.LoginState == VivoxUnity.LoginState.LoggedIn)
        {
            OnUserLoggedIn();
            InputUserName.text = VivoxVoiceManager.Instance.LoginSession.Key.DisplayName;
        }
        else
        {
            OnUserLoggedOut();
        }

    }

    #region Login Kısmı
    public void UserNameChange()
    {
        LoginButton.interactable = InputUserName.text.Trim().Length > 0;
    }

    public void Login()
    {
        LoginButton.interactable = false;

        VivoxVoiceManager.Instance.Login(InputUserName.text.Trim());
    }

    private void OnUserLoggedIn()
    {
        PnlLogin.SetActive(false);
        PnlLobby.SetActive(true);

        var lobbychannel = VivoxVoiceManager.Instance.ActiveChannels.FirstOrDefault(ac => ac.Channel.Name == LobbyChannelName);
        if ((VivoxVoiceManager.Instance && VivoxVoiceManager.Instance.ActiveChannels.Count == 0)
            || lobbychannel == null)
        {
            VivoxVoiceManager.Instance.JoinChannel(LobbyChannelName, ChannelType.NonPositional, VivoxVoiceManager.ChatCapability.TextAndAudio);
        }
        else
        {
            if (lobbychannel.AudioState == ConnectionState.Disconnected)
            {
                // Ask for hosts since we're already in the channel and part added won't be triggered.

                lobbychannel.BeginSetAudioConnected(true, true, ar =>
                {
                    Debug.Log("Now transmitting into lobby channel");
                });
            }

        }

        if (VivoxVoiceManager.Instance.ActiveChannels.Count > 0)
        {
            var LobbyChannel = VivoxVoiceManager.Instance.ActiveChannels.FirstOrDefault(ac => ac.Channel.Name == LobbyChannelName);
            _lobbyChannelId = LobbyChannel.Key;

            // kanaldaki kullanıcıları topla
            foreach (var participant in VivoxVoiceManager.Instance.LoginSession.GetChannelSession(LobbyChannel.Channel).Participants)
            {
                UpdateParticipants(participant, participant.ParentChannelSession.Channel, true);
            }
        }
    }

    private void OnUserLoggedOut()
    {
        PnlLogin.SetActive(true);
        PnlLobby.SetActive(false);
    }
    #endregion

    #region Lobby kısmı
    public void MessageTextChange()
    {
        SendButton.interactable = InputMessage.text.Trim().Length > 0;
        SendTTSButton.interactable = InputMessage.text.Trim().Length > 0;
    }

    public void Logout()
    {
        VivoxVoiceManager.Instance.Logout();
    }

    // Mesaj Gönderme/Alma
    public void SendTextMessage()
    {
        VivoxVoiceManager.Instance.SendTextMessage(InputMessage.text.Trim(), _lobbyChannelId);

        InputMessage.text = "";
    }

    public void SendTTSMessage()
    {
        var ttsMessage = new TTSMessage(InputMessage.text.Trim(), TTSDestination.QueuedRemoteTransmissionWithLocalPlayback);
        VivoxVoiceManager.Instance.LoginSession.TTS.Speak(ttsMessage);

        InputMessage.text = "";

    }

    private void OnTextMessageLogReceivedEvent(string sender, IChannelTextMessage channelTextMessage)
    {
        if (!String.IsNullOrEmpty(channelTextMessage.ApplicationStanzaNamespace))
        {
            // If we find a message with an ApplicationStanzaNamespace we don't push that to the chat box.
            // Such messages denote opening/closing or requesting the open status of multiplayer matches.
            return;
        }

        var newMessageObj = Instantiate(MessageObject, ChatContentObj.transform);
        _messageObjPool.Add(newMessageObj);
        Text newMessageText = newMessageObj.GetComponent<Text>();

        if (channelTextMessage.FromSelf)
        {
            newMessageText.alignment = TextAnchor.MiddleRight;
            newMessageText.text = string.Format($"{channelTextMessage.Message} :<color=blue>{sender} </color>\n<color=#5A5A5A><size=24>{channelTextMessage.ReceivedTime}</size></color>");
        }
        else
        {
            newMessageText.alignment = TextAnchor.MiddleLeft;
            newMessageText.text = string.Format($"<color=green>{sender} </color>: {channelTextMessage.Message}\n<color=#5A5A5A><size=24>{channelTextMessage.ReceivedTime}</size></color>");
            if (ReadIncoming.isOn)
            {
                // Speak local tts message with incoming text message
                new TTSMessage($"{sender} said,", TTSDestination.QueuedLocalPlayback).Speak(VivoxVoiceManager.Instance.LoginSession);
                new TTSMessage($"{channelTextMessage.Message}", TTSDestination.QueuedLocalPlayback).Speak(VivoxVoiceManager.Instance.LoginSession);
            }
        }
    }

    // Bağlantı durumu
    private void OnRecoveryStateChanged(ConnectionRecoveryState recoveryState)
    {
        Color indicatorColor;
        switch (recoveryState)
        {
            case ConnectionRecoveryState.Connected:
                indicatorColor = Color.green;
                break;
            case ConnectionRecoveryState.Disconnected:
                indicatorColor = Color.red;
                break;
            case ConnectionRecoveryState.FailedToRecover:
                indicatorColor = Color.black;
                break;
            case ConnectionRecoveryState.Recovered:
                indicatorColor = Color.green;
                break;
            case ConnectionRecoveryState.Recovering:
                indicatorColor = Color.yellow;
                break;
            default:
                indicatorColor = Color.white;
                break;
        }

        _connectionIndicatorDotImage.color = indicatorColor;
        _connectionIndicatorDotText.text = recoveryState.ToString();
    }

    // Kullanıcılarla ilgili kısımlar
    void OnParticipantAdded(string userName, ChannelId channel, IParticipant participant)
    {
        Debug.Log("OnPartAdded: " + userName);
        UpdateParticipants(participant, channel, true);
    }

    void OnParticipantRemoved(string userName, ChannelId channel, IParticipant participant)
    {
        Debug.Log("OnPartRemoved: " + participant.Account.Name);
        UpdateParticipants(participant, channel, false);
    }

    private void UpdateParticipants(IParticipant participant, ChannelId channel, bool isAddParticipant)
    {
        if (isAddParticipant)
        {
            GameObject newRosterObject = GameObject.Instantiate(PlayerDisplayPrefab, PlayerDisplay);
            RosterItem newRosterItem = newRosterObject.GetComponent<RosterItem>();
            List<RosterItem> thisChannelList;

            if (rosterObjects.ContainsKey(channel))
            {
                //Add this object to an existing roster
                rosterObjects.TryGetValue(channel, out thisChannelList);
                newRosterItem.SetupRosterItem(participant);
                thisChannelList.Add(newRosterItem);
                rosterObjects[channel] = thisChannelList;
            }
            else
            {
                //Create a new roster to add this object to
                thisChannelList = new List<RosterItem>();
                thisChannelList.Add(newRosterItem);
                newRosterItem.SetupRosterItem(participant);
                rosterObjects.Add(channel, thisChannelList);
            }

            ClearParticipants(channel);
        }
        else
        {
            if (rosterObjects.ContainsKey(channel))
            {
                RosterItem removedItem = rosterObjects[channel].FirstOrDefault(p => p.Participant.Account.Name == participant.Account.Name);
                if (removedItem != null)
                {
                    rosterObjects[channel].Remove(removedItem);
                    Destroy(removedItem.gameObject);
                    ClearParticipants(channel);
                }
                else
                {
                    Debug.LogError("Trying to remove a participant that has no roster item.");
                }
            }
        }
    }

    private void ClearParticipants(ChannelId channel)
    {
        RectTransform rt = this.gameObject.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, rosterObjects[channel].Count * 50);
    }
    #endregion
}