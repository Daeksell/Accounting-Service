String.prototype.repeatify = function(times) {
    return Array.from({length:times}, () => this).join('')
};


console.assert('ababab' === 'ab'.repeatify(3))
console.assert('Hello!Hello!Hello!Hello!Hello!Hello!' === 'Hello!'.repeatify(6))
console.assert('' === ''.repeatify(3000000))
console.assert('' === 'Abrakadabra'.repeatify(0))
