function listenWebSocket() {
    let state = {};
    let ws = new WebSocket('ws://' + location.host + '/ws');
    ws.onopen = function () {
        ws.send(JSON.stringify({ listen: 'Keyboard' }));
    };
    ws.onmessage = function (event) {
        let data = JSON.parse(event.data);

        if (data.type == 'Ping') {
            // reply to ping messages
            ws.send(JSON.stringify({ ping: data.ping }));
            return;
        }

        console.log(event);

        if (data.pressed) {
            state[data.button] = true;
            let element = document.getElementById(data.button);
            if (element) {
                element.classList.remove('released');
                element.classList.add('pressed');

                let n = element.lastElementChild;
                n.innerHTML = data.presses;
            }
        } else {
            if (state[data.button]) {
                delete state[data.button];
                let element = document.getElementById(data.button);
                if (element) {
                    element.classList.remove('pressed');
                    element.classList.add('released');
                }
            }
        }
    };
}

document.addEventListener('DOMContentLoaded', function () {
    listenWebSocket();
});