import { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Counter - SemanticHub',
};

export default function CounterLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return children;
}
