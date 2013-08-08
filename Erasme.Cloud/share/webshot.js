var page = require('webpage').create(),
    system = require('system'),
    address, output, size, delay;

if(system.args.length < 3 || system.args.length > 5) {
	console.log('Usage: webshot.js URL filename [widthxheight] [delayms]');
	phantom.exit(1);
}
else {
	address = system.args[1];
	output = system.args[2];
	page.viewportSize = { width: 1024, height: 768 };
	if(system.args.length > 3) {
		size = system.args[3].split('x');
		page.zoomFactor = (new Number(size[0]))/1024;
		page.clipRect = { top: 0, left: 0, width: new Number(size[0]), height: new Number(size[1]) };
		page.viewportSize = { width: new Number(size[0]), height: new Number(size[1]) };
    }
	if(system.args.length > 4)
		delay = new Number(system.args[4]);
    page.open(address, function (status) {
        if (status !== 'success') {
			// Unable to load the address!
			phantom.exit(1);
        }
		else {
            window.setTimeout(function () {
                page.render(output);
                phantom.exit();
            }, delay);
        }
    });
}
