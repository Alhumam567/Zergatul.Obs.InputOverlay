function displayFps() {
    let interval = 1000;
    let list = [];
    let div = document.createElement('div');
    div.style.position = 'absolute';
    div.style.left = '0';
    div.style.top = '0';
    div.style.fontSize = '50px';
    div.style.color = 'white';
    document.body.appendChild(div);
    let callback = function () {
        let now = window.performance.now();
        if (list.length > 0) {
            let elapsed = now - list[0];
            let fps = 1000 * list.length / elapsed;
            div.innerHTML = 'FPS: ' + fps.toFixed(1);
            let count = 0;
            while (count < list.length && now - list[count] > interval) {
                count++;
            }
            if (count == list.length) {
                count--;
            }
            list.splice(0, count);
        }
        list.push(now);
        window.requestAnimationFrame(callback);
    };
    window.requestAnimationFrame(callback);
}
