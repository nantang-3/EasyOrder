// EasyOrder 휴대폰 페이지 (IR-08, FR-14)
// - WebSocket(/ws)으로 키오스크와 연결하고, 끊기면 2초 후 다시 연결한다.
// - "음성주문활성화"를 누른 뒤에만 음성을 재생한다.
// - 버튼은 한 번 누르면 "활성화됨"으로 바뀌고 이후에는 반응하지 않는다.
(() => {
  'use strict';

  const button = document.getElementById('activate');
  const audio = new Audio();
  let socket = null;
  let activated = false;
  let activationSent = false;
  let audioReady = Promise.resolve();

  function connect() {
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${scheme}://${location.host}/ws`);
    socket.addEventListener('open', () => {
      console.log('[EasyOrder] 연결됨');
      // 연결되기 전에 버튼을 눌렀다면 연결 직후 보낸다
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
      }
    });
    socket.addEventListener('close', () => {
      console.log('[EasyOrder] 연결 끊김, 2초 후 재연결');
      setTimeout(connect, 2000);
    });
  }

  function sendActivation() {
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({ type: 'voice-activated' }));
      activationSent = true;
    }
  }

  // 버튼 터치(사용자 동작) 안에서 오디오를 한 번 무음 재생해 자동 재생 제한을 확실히 해제한다
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
    if (!activated || typeof file !== 'string' || !/^[a-z0-9-]+\.wav$/.test(file)) {
      return;
    }
    audioReady.then(() => {
      audio.src = `audio/${file}`;
      return audio.play();
    }).catch((err) => console.warn('[EasyOrder] 재생 실패', err));
  }

  button.addEventListener('click', () => {
    if (activated) {
      return;
    }
    activated = true;
    button.disabled = true;
    button.textContent = '활성화됨';
    button.classList.add('activated');
    audioReady = unlockAudio();
    sendActivation();
  });

  connect();
})();
