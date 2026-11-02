import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Testing Library only auto-cleans when the runner's afterEach is global.
afterEach(() => {
    cleanup();
});

// jsdom has no layout. The municipality picker scrolls the active option into view.
Element.prototype.scrollIntoView = () => {};
