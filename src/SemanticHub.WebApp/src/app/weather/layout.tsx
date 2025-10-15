import { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Weather - SemanticHub',
};

export default function WeatherLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return children;
}
