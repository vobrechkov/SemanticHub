'use client';

import { useState } from 'react';
import { Button } from 'react-bootstrap';

export default function CounterPage() {
  const [count, setCount] = useState(0);

  const incrementCount = () => {
    setCount(count + 1);
  };

  return (
    <div>
      <h1>Counter</h1>

      <p role="status">Current count: {count}</p>

      <Button variant="primary" onClick={incrementCount}>
        Click me
      </Button>
    </div>
  );
}
