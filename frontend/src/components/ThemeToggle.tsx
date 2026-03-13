import { useEffect, useState } from 'react';

export function ThemeToggle() {
  const [dark, setDark] = useState(true);
  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark);
  }, [dark]);

  return (
    <button className="rounded border px-3 py-2" onClick={() => setDark((x) => !x)}>
      {dark ? 'Dark' : 'Light'} Mode
    </button>
  );
}
