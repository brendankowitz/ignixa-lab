import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import { BenchesApp } from './benches/BenchesApp';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BenchesApp />
  </StrictMode>,
);
