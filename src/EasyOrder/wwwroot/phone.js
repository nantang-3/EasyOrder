// EasyOrder 휴대폰 페이지 (IR-08, FR-14)
// - WebSocket(/ws)으로 키오스크와 연결하고, 끊기면 2초 후 다시 연결한다.
// - "음성주문활성화"를 누른 뒤에만 음성을 재생한다.
//   (버튼 터치가 브라우저 자동 재생 제한을 해제하는 사용자 동작이 된다)
(() => {
  'use strict';

  const button = document.getElementById('activate');
  const audio = new Audio();
  let socket = null;
  let activated = false;

  function connect() {
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${scheme}://${location.host}/ws`);
    socket.addEventListener('open', () => console.log('[EasyOrder] 연결됨'));
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

  function send(message) {
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
    }
  }

  function play(file) {
    if (!activated || typeof file !== 'string' || !/^[a-z0-9-]+\.wav$/.test(file)) {
      return;
    }
    audio.src = `audio/${file}`;
    audio.play().catch((err) => console.warn('[EasyOrder] 재생 실패', err));
  }

  button.addEventListener('click', () => {
    activated = true;
    send({ type: 'voice-activated' });
  });

  connect();
})();
