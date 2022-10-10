﻿let svgDoc = null;
const buttonColor = 'crimson';
const releaseFadeTime = '250ms';
const scrollAnimationTime = '350'; // milliseconds

function addSvgReleased(element) {
    let animate = document.createElementNS('http://www.w3.org/2000/svg', 'animate');
    animate.setAttribute('id', element.id + '-animate');
    animate.setAttribute('attributeType', 'XML');
    animate.setAttribute('attributeName', 'fill-opacity');
    animate.setAttribute('values', '1.0; 0.0');
    animate.setAttribute('dur', releaseFadeTime);
    animate.setAttribute('fill', 'freeze');

    element.appendChild(animate);
    animate.beginElement();
}

function initScroll() {
    let scroll1 = svgDoc.getElementById('MouseScrollUp');
    scroll1.setAttribute('opacity', 0);
    let scroll2 = svgDoc.getElementById('MouseScrollDown');
    scroll2.setAttribute('opacity', 0);
}

function removeSvgAnimations(element) {
    for (let i = 0; i < element.children.length; i++) {
        if (element.children[i].tagName == 'animateTransform' || element.children[i].tagName == 'animate') {
            element.removeChild(element.children[i]);
            i--;
        }
    }
}

let scrollUpTimeoutId = 0;
let scrollDownTimeoutId = 0;

function processScroll(id, dy, setupTimeout) {
    let scroll = svgDoc.getElementById(id);
    removeSvgAnimations(scroll);

    let animateTransform = document.createElementNS('http://www.w3.org/2000/svg', 'animateTransform');
    animateTransform.setAttribute('attributeName', 'transform');
    animateTransform.setAttribute('attributeType', 'XML');
    animateTransform.setAttribute('type', 'translate');
    animateTransform.setAttribute('from', '0 0');
    animateTransform.setAttribute('to', '0 ' + dy);
    animateTransform.setAttribute('dur', scrollAnimationTime + 'ms');
    animateTransform.setAttribute('repeatCount', '1');

    var animate = document.createElementNS('http://www.w3.org/2000/svg', 'animate');
    animate.setAttribute('attributeType', 'XML');
    animate.setAttribute('attributeName', 'opacity');
    animate.setAttribute('values', '1.0; 0.0');
    animate.setAttribute('dur', scrollAnimationTime + 'ms');
    animate.setAttribute('repeatCount', '1');

    scroll.appendChild(animateTransform);
    scroll.appendChild(animate);
    animateTransform.beginElement();
    animate.beginElement();

    setupTimeout(function () {
        removeSvgAnimations(scroll);
        scroll.setAttribute('opacity', 0);
    }, scrollAnimationTime);
};

function scrollUp() {
    processScroll('MouseScrollUp', '-10', function (callback, delay) {
        clearTimeout(scrollUpTimeoutId);
        scrollUpTimeoutId = setTimeout(callback, delay);
    });
};

function scrollDown() {
    processScroll('MouseScrollDown', '10', function (callback, delay) {
        clearTimeout(scrollDownTimeoutId);
        scrollDownTimeoutId = setTimeout(callback, delay);
    });
};

function attachSvgGlowDefinition(svgDoc) {

    let svg = svgDoc.rootElement;

    let defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');

    let filter = document.createElementNS('http://www.w3.org/2000/svg', 'filter');
    filter.id = 'glow';
    filter.setAttribute('filterUnits', 'userSpaceOnUse');
    filter.setAttribute('x', '-50%');
    filter.setAttribute('y', '-50%');
    filter.setAttribute('width', '200%');
    filter.setAttribute('height', '200%');

    let gaussianBlur = document.createElementNS('http://www.w3.org/2000/svg', 'feGaussianBlur');
    gaussianBlur.setAttribute('in', 'SourceGraphic');
    gaussianBlur.setAttribute('stdDeviation', 10);
    gaussianBlur.setAttribute('result', 'blur20');

    let colorMatrix = document.createElementNS('http://www.w3.org/2000/svg', 'feColorMatrix');
    colorMatrix.setAttribute('in', 'blur20');
    colorMatrix.setAttribute('type', 'matrix');
    colorMatrix.setAttribute('values',
        '1 0 0 2 0 ' +
        '0 1 0 2 0 ' +
        '0 0 1 2 0 ' +
        '0 0 0 1 0');
    colorMatrix.setAttribute('result', 'blur-colored');

    let merge = document.createElementNS('http://www.w3.org/2000/svg', 'feMerge');
    let mergeNode1 = document.createElementNS('http://www.w3.org/2000/svg', 'feMergeNode');
    mergeNode1.setAttribute('in', 'blur-colored');
    let mergeNode2 = document.createElementNS('http://www.w3.org/2000/svg', 'feMergeNode');
    mergeNode2.setAttribute('in', 'SourceGraphic');
    merge.appendChild(mergeNode1);
    merge.appendChild(mergeNode2);

    filter.appendChild(gaussianBlur);
    filter.appendChild(colorMatrix);
    filter.appendChild(merge);

    defs.appendChild(filter);

    svg.appendChild(defs);
}

function onSvgLoaded() {
    let object = document.getElementsByTagName('object')[0];
    svgDoc = object.contentDocument;
    attachSvgGlowDefinition(svgDoc);

    initScroll();
    listenWebSocket();
}

function listenWebSocket() {
    let ws = new WebSocket('ws://' + location.host + '/ws');
    ws.onopen = function () {
        ws.send(JSON.stringify({ listen: 'MouseButtons' }));
    };
    ws.onmessage = function (event) {
        let data = JSON.parse(event.data);

        if (data.type == 'Ping') {
            // reply to ping messages
            ws.send(JSON.stringify({ ping: data.ping }));
            return;
        }

        console.log(event);

        if (data.button == 'MouseWheelUp') {
            scrollUp();
        } else if (data.button == 'MouseWheelDown') {
            scrollDown();
        } else {
            if (data.pressed) {
                let element = svgDoc.getElementById(data.button);

                let n = document.getElementById(data.button + "P");
                n.innerHTML = data.presses;

                if (element) {
                    element.style.fill = buttonColor;
                    element.style.filter = 'url(#glow)';
                    removeSvgAnimations(element);
                }
            } else {
                let element = svgDoc.getElementById(data.button);
                if (element) {
                    element.style.fill = buttonColor;
                    element.style.filter = 'none';
                    addSvgReleased(element);
                }
            }
        }
    };
}

document.addEventListener('DOMContentLoaded', function () {
    let object = document.getElementsByTagName('object')[0];
    if (object.contentDocument && object.contentDocument.rootElement) {
        onSvgLoaded();
    } else {
        object.addEventListener('load', onSvgLoaded);
    }
});
