// EasyOrder 휴대폰 페이지 (IR-05, IR-08, FR-06, FR-14, FR-16)
// - WebSocket(/ws)으로 키오스크와 연결하고, 끊기면 2초 후 다시 연결한다.
// - "음성주문활성화"를 누른 뒤에만 음성을 재생한다. 버튼은 "활성화됨"으로 바뀌고 이후 반응하지 않는다.
// - 활성화 후 "대답하기"를 누르면 녹음(Web Speech API, ko-KR)을 시작하고,
//   한 번 더 누르면 녹음을 끝내고 인식된 텍스트를 키오스크로 보낸다.
// - 키오스크가 voice-stopped를 보내면(3회 실패) 대답하기를 비활성화한다.
// - 키오스크가 answer-label을 보내면 버튼 문구를 바꾼다(포장/매장 단계 "대답하기", 메뉴 단계 "주문하기").
(() => {
  'use strict';

  const activateButton = document.getElementById('activate');
  const answerRow = document.getElementById('answer-row');
  const answerButton = document.getElementById('answer');
  const mic = document.getElementById('mic');
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

  const audio = new Audio();
  let socket = null;
  let activated = false;
  let activationSent = false;
  let audioReady = Promise.resolve();

  let recognition = null;
  let transcript = [];
  let recognitionError = null;
  let voiceStopped = false;

  // ── 연결 ──
  function connect() {
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${scheme}://${location.host}/ws`);
    socket.addEventListener('open', () => {
      console.log('[EasyOrder] 연결됨');
      if (activated && !activationSent) {
        sendActivation();
      }
    });
    socket.addEventListener('message', (event) => {
      let message;
      try {
        message = JSON.parse(event.data);
      } catch {
        return;
      }
      if (message.type === 'play') {
        play(message.file);
      } else if (message.type === 'voice-stopped') {
        stopVoiceInput();
      } else if (message.type === 'answer-label' && typeof message.label === 'string') {
        answerButton.textContent = message.label;
      }
    });
    socket.addEventListener('close', () => {
      console.log('[EasyOrder] 연결 끊김, 2초 후 재연결');
      setTimeout(connect, 2000);
    });
  }

  function send(message) {
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
      return true;
    }
    return false;
  }

  function sendActivation() {
    activationSent = send({ type: 'voice-activated' });
  }

  // ── 음성 출력 ──
  function unlockAudio() {
    audio.muted = true;
    audio.src = 'audio/retry.wav';
    return audio.play()
      .then(() => audio.pause())
      .catch((err) => console.warn('[EasyOrder] 오디오 활성화 실패', err))
      .finally(() => {
        audio.currentTime = 0;
        audio.muted = false;
      });
  }

  function play(file) {
    if (!activated || typeof file !== 'string' || !/^(tts\/)?[a-z0-9-]+\.wav$/.test(file)) {
      return;
    }
    audioReady.then(() => {
      audio.src = `audio/${file}`;
      return audio.play();
    }).catch((err) => console.warn('[EasyOrder] 재생 실패', err));
  }

  // ── 음성 입력 (대답하기) ──
  function setListening(listening) {
    mic.classList.toggle('listening', listening);
  }

  function startListening() {
    if (!SpeechRecognition) {
      console.warn('[EasyOrder] 이 브라우저는 음성 인식을 지원하지 않습니다');
      return;
    }
    // 녹음 시작 전에 재생 중인 안내 음성은 멈춘다
    audio.pause();

    transcript = [];
    recognitionError = null;
    recognition = new SpeechRecognition();
    recognition.lang = 'ko-KR';
    recognition.continuous = true;
    recognition.interimResults = false;
    recognition.onresult = (event) => {
      for (let i = event.resultIndex; i < event.results.length; i++) {
        if (event.results[i].isFinal) {
          transcript.push(event.results[i][0].transcript);
        }
      }
    };
    recognition.onerror = (event) => {
      recognitionError = event.error;
      console.warn('[EasyOrder] 음성 인식 오류', event.error);
    };
    recognition.onend = () => {
      recognition = null;
      setListening(false);
      // 마이크 권한 거부 등 녹음 자체가 안 된 경우는 보내지 않는다
      if (['not-allowed', 'service-not-allowed', 'audio-capture'].includes(recognitionError)) {
        return;
      }
      if (!voiceStopped) {
        send({ type: 'recognized', text: transcript.join(' ').trim() });
      }
    };
    recognition.start();
    setListening(true);
  }

  function stopListening() {
    if (recognition) {
      recognition.stop();
    }
  }

  function stopVoiceInput() {
    voiceStopped = true;
    answerButton.disabled = true;
    if (recognition) {
      recognition.abort();
    }
    setListening(false);
  }

  // ── 버튼 ──
  activateButton.addEventListener('click', () => {
    if (activated) {
      return;
    }
    activated = true;
    activateButton.disabled = true;
    activateButton.textContent = '활성화됨';
    activateButton.classList.add('activated');
    answerRow.hidden = false;
    audioReady = unlockAudio();
    sendActivation();
  });

  answerButton.addEventListener('click', () => {
    if (voiceStopped) {
      return;
    }
    if (recognition) {
      stopListening();
    } else {
      startListening();
    }
  });

  connect();
})();
